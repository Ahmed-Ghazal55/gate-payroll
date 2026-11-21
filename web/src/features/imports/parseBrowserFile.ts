/** Browser parsers for json-rows company imports (xlsx / csv / json). */

export async function parseBrowserFileToRows(file: File): Promise<Record<string, unknown>[]> {
  const name = file.name.toLowerCase();

  if (name.endsWith('.json')) {
    const text = await file.text();
    const data = JSON.parse(text);
    if (Array.isArray(data)) return data;
    if (data && Array.isArray(data.companies)) return data.companies;
    if (data && Array.isArray(data.rows)) return data.rows;
    throw new Error('ملف JSON يجب أن يحتوي على مصفوفة شركات أو حقل companies/rows.');
  }

  if (name.endsWith('.csv')) {
    const text = await file.text();
    const lines = text.split(/\r?\n/).filter((l) => l.trim().length > 0);
    if (lines.length < 2) throw new Error('ملف CSV فارغ أو لا يحتوي على صفوف بيانات.');
    const headers = lines[0].split(',').map((h) => h.trim().replace(/^["']|["']$/g, ''));
    const rows: Record<string, unknown>[] = [];
    for (let i = 1; i < lines.length; i++) {
      const cells = lines[i].split(',').map((c) => c.trim().replace(/^["']|["']$/g, ''));
      if (cells.every((c) => c === '')) continue;
      const row: Record<string, unknown> = {};
      headers.forEach((h, idx) => {
        row[h] = cells[idx] ?? '';
      });
      if (!Object.values(row).some((v) => typeof v === 'string' && v.includes('مثال — احذف'))) {
        rows.push(row);
      }
    }
    return rows;
  }

  const arrayBuffer = await file.arrayBuffer();
  const view = new DataView(arrayBuffer);
  let offset = 0;
  const files: Record<string, string> = {};

  while (offset + 30 <= arrayBuffer.byteLength) {
    const sig = view.getUint32(offset, true);
    if (sig !== 0x04034b50) break;
    const compMethod = view.getUint16(offset + 8, true);
    const compSize = view.getUint32(offset + 18, true);
    const fnLen = view.getUint16(offset + 26, true);
    const extraLen = view.getUint16(offset + 28, true);
    const fnBytes = new Uint8Array(arrayBuffer, offset + 30, fnLen);
    const fn = new TextDecoder().decode(fnBytes);
    const dataOffset = offset + 30 + fnLen + extraLen;
    const compData = new Uint8Array(arrayBuffer, dataOffset, compSize);

    if (fn.startsWith('xl/worksheets/sheet') || fn.includes('sharedStrings')) {
      let content = '';
      if (compMethod === 0) {
        content = new TextDecoder().decode(compData);
      } else if (compMethod === 8) {
        const ds = new DecompressionStream('deflate-raw');
        const writer = ds.writable.getWriter();
        writer.write(compData);
        writer.close();
        const reader = ds.readable.getReader();
        const chunks: Uint8Array[] = [];
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          chunks.push(value);
        }
        const total = chunks.reduce((a, b) => a + b.length, 0);
        const merged = new Uint8Array(total);
        let pos = 0;
        for (const c of chunks) {
          merged.set(c, pos);
          pos += c.length;
        }
        content = new TextDecoder().decode(merged);
      }
      files[fn] = content;
    }
    offset = dataOffset + compSize;
  }

  const shared: string[] = [];
  if (files['xl/sharedStrings.xml']) {
    const sxml = files['xl/sharedStrings.xml'];
    const siRegex = /<si\b[^>]*>(.*?)<\/si>/gs;
    let match: RegExpExecArray | null;
    while ((match = siRegex.exec(sxml)) !== null) {
      const tMatch = /<t\b[^>]*>(.*?)<\/t>/s.exec(match[1]);
      shared.push(tMatch ? tMatch[1] : '');
    }
  }

  const sheetXml = files['xl/worksheets/sheet1.xml'] || Object.values(files)[0] || '';
  if (!sheetXml) throw new Error('لم يتم العثور على ورقة البيانات داخل مصنف Excel.');

  const rowRegex = /<row\b[^>]*>(.*?)<\/row>/gs;
  const parsedRows: Record<string, string>[] = [];
  let rowMatch: RegExpExecArray | null;

  while ((rowMatch = rowRegex.exec(sheetXml)) !== null) {
    const rowContent = rowMatch[1];
    const cellRegex = /<c\b([^>]*)>(.*?)<\/c>/gs;
    const cells: Record<string, string> = {};
    let cellMatch: RegExpExecArray | null;

    while ((cellMatch = cellRegex.exec(rowContent)) !== null) {
      const attrs = cellMatch[1];
      const body = cellMatch[2];
      const rAttr = /r="([A-Z]+)(\d+)"/.exec(attrs);
      if (!rAttr) continue;
      const colLetter = rAttr[1];
      const isString = /t="s"/.test(attrs);
      const isInline = /t="inlineStr"/.test(attrs);
      let val = '';
      if (isString) {
        const vMatch = /<v>(.*?)<\/v>/.exec(body);
        if (vMatch) val = shared[parseInt(vMatch[1], 10)] || '';
      } else if (isInline) {
        const tMatch = /<t\b[^>]*>(.*?)<\/t>/s.exec(body);
        if (tMatch) val = tMatch[1];
      } else {
        const vMatch = /<v>(.*?)<\/v>/.exec(body);
        if (vMatch) val = vMatch[1];
      }
      cells[colLetter] = val;
    }
    parsedRows.push(cells);
  }

  if (parsedRows.length < 2) throw new Error('ملف Excel فارغ أو لا يحتوي على صفوف بيانات كافية.');

  function colToIdx(col: string): number {
    let num = 0;
    for (let i = 0; i < col.length; i++) num = num * 26 + (col.charCodeAt(i) - 64);
    return num - 1;
  }

  const headerMap: Record<number, string> = {};
  for (const [col, val] of Object.entries(parsedRows[0])) {
    headerMap[colToIdx(col)] = val.trim();
  }

  const result: Record<string, unknown>[] = [];
  for (let r = 1; r < parsedRows.length; r++) {
    const rowObj: Record<string, unknown> = {};
    let hasAnyVal = false;
    for (const [col, val] of Object.entries(parsedRows[r])) {
      const header = headerMap[colToIdx(col)];
      if (header) {
        rowObj[header] = val;
        if (val) hasAnyVal = true;
      }
    }
    if (hasAnyVal && !Object.values(rowObj).some((v) => typeof v === 'string' && v.includes('مثال — احذف'))) {
      result.push(rowObj);
    }
  }
  return result;
}

export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      resolve(result.includes(',') ? result.split(',')[1] : result);
    };
    reader.onerror = (err) => reject(err);
    reader.readAsDataURL(file);
  });
}

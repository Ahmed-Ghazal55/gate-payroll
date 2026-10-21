"""Fetch OFL fonts from a pinned Google Fonts revision; no public form calls a CDN."""
import hashlib,json,urllib.request,urllib.parse
from pathlib import Path
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'scripts/fonts-manifest.json').read_text())
out=root/'Payroll.Api/wwwroot/fonts';out.mkdir(parents=True,exist_ok=True)
for item in manifest['files']:
    target=out/item['file']
    def valid(data): return hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()==item['blob']
    if target.exists() and valid(target.read_bytes()): continue
    url='https://raw.githubusercontent.com/'+manifest['repository']+'/'+manifest['commit']+'/'+urllib.parse.quote(item['path'])
    with urllib.request.urlopen(url,timeout=45) as response: data=response.read(5000000)
    assert valid(data),'Font integrity mismatch: '+item['file']
    target.write_bytes(data)
print('PASS pinned Cairo, Outfit and Noto Nastaliq Urdu fonts and OFL licenses')

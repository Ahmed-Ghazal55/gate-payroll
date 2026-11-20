// Seed legacy payroll `work` rows the same shape free-upload used to write.
// Page buttons no longer expose free-form /api/imports/inspect|preview|commit;
// HTTP tests insert the document store rows directly (same pattern as r6c/finance fixtures).
import {createHash} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import path from 'node:path';

function workId({period,platform,sourceRef,iqama,accountId,from,to}){
 return createHash('sha256').update(`${period}|${platform}|${sourceRef}|${iqama}|${accountId}|${from}|${to}`).digest('hex');
}

/** @param {string} dataDir PAYROLL_DATA_DIR for the running test server */
/** @param {Array<object>} rows camelCase work fields (period, platform, iqama, …) */
export function seedWorkRows(dataDir, rows){
 const db=new DatabaseSync(path.join(dataDir,'payroll.db'));
 try{
  const put=db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET body=excluded.body');
  const ids=[];
  for(const row of rows){
   const from=row.from||`${row.period}-01`;
   const to=row.to||new Date(Date.UTC(Number(row.period.slice(0,4)),Number(row.period.slice(5,7)),0)).toISOString().slice(0,10);
   const sourceRef=row.sourceRef||`${row.platform}:${row.accountId||''}:${row.iqama}:${from}:${to}`;
   const id=workId({period:row.period,platform:row.platform,sourceRef,iqama:row.iqama,accountId:row.accountId||'',from,to});
   const doc={
    id,uploadId:row.uploadId||'synthetic-seed',
    sourceRef,iqama:row.iqama,name:row.name||'',platform:row.platform,accountId:row.accountId||'',
    period:row.period,from,to,orders:row.orders??0,
    platformRevenue:row.platformRevenue??null,riderPay:row.riderPay??null,
    isValid:row.isValid??null,company:row.company||'',city:row.city||'',datesConfirmed:!!(row.from&&row.to)
   };
   put.run(id,'work',JSON.stringify(doc));ids.push(id);
  }
  return ids;
 }finally{db.close();}
}

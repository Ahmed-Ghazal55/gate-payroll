// Seed legacy payroll `work` rows through the active store (Development-only /api/test/seed-work).
// Never write payroll.db directly: SQL Server CI uses ConnectionStrings__DefaultConnection.
/** @param {(route:string,body?:object,expected?:number)=>Promise<any>} req authenticated HTTP helper */
/** @param {Array<object>} rows camelCase work fields (period, platform, iqama, …) */
export async function seedWorkRows(req, rows){
 const result=await req('/api/test/seed-work',{rows});
 const ids=result?.ids??[];
 if(!Array.isArray(ids)||ids.length!==rows.length)throw new Error('seed-work returned unexpected ids: '+JSON.stringify(result));
 return ids;
}

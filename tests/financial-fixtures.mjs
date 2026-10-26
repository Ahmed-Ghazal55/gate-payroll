// Synthetic cash is introduced by the same reviewed opening flow used by an administrator.
// It is never injected by a default company assumption in production code.
export async function seedAccountingCash(req) {
  const rows=['BW','EX','TR'].flatMap(sponsorCode=>[`MAIN.${sponsorCode}.DEBT`,`BANK.${sponsorCode}`].map(accountCode=>({accountCode,sponsorCode,iqama:'',verifiedBalance:100000,sourceRef:`synthetic-opening:${accountCode}`,notes:'Synthetic verified cash for isolated financial integration tests'})));
  const preview=await req('/api/finance/opening/preview',{rows});
  await req('/api/finance/opening/commit',{rows:preview.rows,fingerprint:preview.fingerprint,confirm:true});
}

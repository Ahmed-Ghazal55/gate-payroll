import openapiTS, {astToString} from 'openapi-typescript';
import {readFile,writeFile} from 'node:fs/promises';
const source=new URL('../../docs/api/openapi.v1.json',import.meta.url);
const target=new URL('../src/lib/api-contract.ts',import.meta.url);
const output=astToString(await openapiTS(JSON.parse(await readFile(source,'utf8'))));
if(process.argv.includes('--check')) {
 if(await readFile(target,'utf8')!==output)throw new Error('API contract changed. Run npm run api:generate in web.');
} else await writeFile(target,output);

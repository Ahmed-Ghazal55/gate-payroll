import { redirect } from 'next/navigation';

/** Shell hidden until platform page is complete — working workspace only. */
export default function Page() {
  redirect('/payroll/?tab=platforms&app=keeta');
}

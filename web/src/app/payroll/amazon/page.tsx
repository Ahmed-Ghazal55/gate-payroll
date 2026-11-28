import { redirect } from 'next/navigation';

/** Redirect direct /payroll/amazon link to the Amazon platform workspace. */
export default function Page() {
  redirect('/payroll/?tab=platforms&app=amazon');
}

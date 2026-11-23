import { redirect } from 'next/navigation';

export default function Page() {
  redirect('/payroll/?tab=platforms&app=chefz');
}

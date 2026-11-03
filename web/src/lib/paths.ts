// Next adds basePath (/app) to <Link> and router paths itself. Server data may still carry
// the /app prefix; strip it before navigation so links never become /app/app/....
export const inApp = (path: string): string => {
  const stripped = path.replace(/^\/app(?=\/|\?|$)/, '');
  if (stripped === '') return '/';
  return stripped.startsWith('?') ? `/${stripped}` : stripped;
};

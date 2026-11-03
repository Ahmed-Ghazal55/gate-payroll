/** @type {import('next').NextConfig} */
const isDev = process.env.NODE_ENV === 'development';

const nextConfig = {
  output: isDev ? undefined : 'export',
  basePath: '/app',
  trailingSlash: true,
  images: {
    unoptimized: true,
  },
  ...(isDev ? {
    async rewrites() {
      return [
        {
          source: '/api/:path*',
          destination: 'http://localhost:5000/api/:path*',
        },
      ];
    },
  } : {}),
};

export default nextConfig;

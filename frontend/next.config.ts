import type { NextConfig } from "next";

const apiOrigin = process.env.API_ORIGIN ?? "https://localhost:7010";
if (new URL(apiOrigin).protocol !== "https:") {
  throw new Error("API_ORIGIN must use HTTPS because the API requires secure cookies and antiforgery tokens.");
}
const nextConfig: NextConfig = {
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiOrigin}/api/:path*` }];
  },
};

export default nextConfig;

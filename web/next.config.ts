import type { NextConfig } from "next";

const config: NextConfig = {
  output: "export",
  trailingSlash: false,
  images: { unoptimized: true },
};

export default config;

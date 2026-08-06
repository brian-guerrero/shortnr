import { defineConfig } from "astro/config";

export default defineConfig({
  site: "https://brian-guerrero.github.io",
  base: "/shortnr",
  trailingSlash: "always",
  build: {
    format: "directory",
  },
  vite: {
    build: {
      assetsInlineLimit: 0,
    },
  },
});

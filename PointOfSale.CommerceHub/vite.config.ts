import path from "path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "url";

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(currentDirectory, "./src")
    }
  },
  server: {
    port: 5175,
    proxy: {
      "/api": {
        target: "http://localhost:5010",
        changeOrigin: true,
        secure: false
      }
    }
  }
});

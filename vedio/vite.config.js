import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "/vedio/",
  server: { proxy: process.env.VEDIO_API_TARGET ? { "/api": { target: process.env.VEDIO_API_TARGET, changeOrigin: true } } : undefined },
  build: {
    rollupOptions: { output: { manualChunks: { react: ["react", "react-dom"] } } },
  },
});

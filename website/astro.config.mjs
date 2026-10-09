import { defineConfig } from 'astro/config';

// 站点托管在 GitHub Pages 的项目页下，地址形如 https://<用户>.github.io/bowen_music/
// base 必须与仓库名一致，否则 dist 里的资源引用会全部 404。
// 本地开发同样带这个前缀：http://localhost:4321/bowen_music/
export default defineConfig({
  site: 'https://ldm0715.github.io',
  base: '/bowen_music',
  output: 'static',
  trailingSlash: 'ignore',
  build: {
    // 产出 index.html 而非 index/index.html，Pages 直接可服务
    format: 'file',
  },
});

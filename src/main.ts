import './style.css';
import { ArcanePuckApp } from './app/ArcanePuckApp';

const container = document.querySelector<HTMLElement>('#app');

if (!container) throw new Error('Missing #app root element');

container.innerHTML = '<div class="loading-screen"><span class="loading-rune">✦</span><p>正在启动决斗大厅…</p></div>';

void ArcanePuckApp.create(container)
  .then(() => container.querySelector('.loading-screen')?.remove())
  .catch((error: unknown) => {
    console.error(error);
    container.innerHTML = `
      <div class="fatal-error">
        <h1>Arcane Puck VR</h1>
        <p>启动失败。请确认浏览器支持 WebAssembly 和 WebGL 2。</p>
      </div>`;
  });


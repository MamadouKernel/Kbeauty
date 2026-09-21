window.kekePwa = (() => {
  let deferredPrompt = null;
  const listeners = new Set();
  window.addEventListener('keke-pwa-update', () => listeners.forEach(dotnet => dotnet.invokeMethodAsync('OnPwaUpdateAvailable').catch(() => {})));
  navigator.serviceWorker?.addEventListener('controllerchange', () => location.reload());
  const notify = () => listeners.forEach(dotnet => dotnet.invokeMethodAsync('OnPwaStateChanged', getState()).catch(() => {}));
  const isStandalone = () => window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
  const markInstalled = () => { localStorage.setItem('keke-pwa-installed', 'true'); notify(); };
  const getState = () => {
    const standalone = isStandalone();
    if (standalone) localStorage.setItem('keke-pwa-installed', 'true');
    return {
      online: navigator.onLine,
      installable: Boolean(deferredPrompt),
      standalone,
      installedKnown: localStorage.getItem('keke-pwa-installed') === 'true',
      ios: /iphone|ipad|ipod/i.test(navigator.userAgent)
    };
  };
  window.addEventListener('beforeinstallprompt', event => { event.preventDefault(); deferredPrompt = event; notify(); });
  window.addEventListener('appinstalled', () => { deferredPrompt = null; markInstalled(); });
  window.addEventListener('online', notify);
  window.addEventListener('offline', notify);
  async function install() {
    if (!deferredPrompt) return { outcome: 'unavailable' };
    await deferredPrompt.prompt();
    const choice = await deferredPrompt.userChoice;
    if (choice.outcome === 'accepted') { deferredPrompt = null; markInstalled(); }
    notify();
    return choice;
  }
  async function register(dotnet) { listeners.add(dotnet); notify(); return getState(); }
  function unregister(dotnet) { listeners.delete(dotnet); }
  function saveClientSnapshot(value) { localStorage.setItem('keke-client-snapshot', JSON.stringify({ savedAt: new Date().toISOString(), value })); }
  function applyUpdate() { navigator.serviceWorker?.getRegistration().then(r => r?.waiting?.postMessage({ type: 'SKIP_WAITING' })); }
  return { getState, install, markInstalled, register, unregister, saveClientSnapshot, applyUpdate };
})();


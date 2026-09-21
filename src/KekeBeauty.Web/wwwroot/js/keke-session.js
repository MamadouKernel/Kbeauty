window.kekeInactivity = (() => {
  const timeoutMs = 30 * 60 * 1000;
  const activityKey = 'keke-last-activity';
  let dotnet = null;
  let timer = null;
  let active = false;
  let expiring = false;
  let lastWrite = 0;

  const schedule = () => {
    clearTimeout(timer);
    if (!active || expiring) return;
    const last = Number(localStorage.getItem(activityKey) || Date.now());
    const remaining = timeoutMs - (Date.now() - last);
    if (remaining <= 0) { expire(); return; }
    timer = setTimeout(expire, remaining);
  };

  const touch = () => {
    if (!active || expiring) return;
    const now = Date.now();
    if (now - lastWrite >= 5000) {
      localStorage.setItem(activityKey, String(now));
      lastWrite = now;
    }
    schedule();
  };

  const expire = async () => {
    if (!active || expiring) return;
    expiring = true;
    clearTimeout(timer);
    try { await dotnet?.invokeMethodAsync('OnSessionExpired'); } catch { expiring = false; schedule(); }
  };

  ['pointerdown', 'keydown', 'touchstart', 'scroll'].forEach(name =>
    window.addEventListener(name, touch, { passive: true }));
  document.addEventListener('visibilitychange', () => { if (!document.hidden) schedule(); });
  window.addEventListener('focus', schedule);
  window.addEventListener('storage', event => { if (event.key === activityKey) schedule(); });

  function register(reference, authenticated) {
    dotnet = reference;
    active = authenticated;
    expiring = false;
    if (active && !localStorage.getItem(activityKey)) localStorage.setItem(activityKey, String(Date.now()));
    schedule();
  }
  function sessionStarted() {
    active = true; expiring = false;
    localStorage.setItem(activityKey, String(Date.now()));
    schedule();
  }
  function sessionEnded() {
    active = false; expiring = false;
    clearTimeout(timer);
    localStorage.removeItem(activityKey);
  }
  function unregister() { dotnet = null; clearTimeout(timer); }
  return { register, sessionStarted, sessionEnded, unregister };
})();

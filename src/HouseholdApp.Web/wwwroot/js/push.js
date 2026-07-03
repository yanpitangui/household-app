function urlBase64ToUint8Array(base64String) {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
  const rawData = atob(base64);
  return Uint8Array.from([...rawData].map((c) => c.charCodeAt(0)));
}

function antiforgeryToken() {
  return document.querySelector("meta[name='request-verification-token']")?.content;
}

async function postJson(url, body) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: antiforgeryToken() },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${url} responded with ${response.status}`);
}

async function initPushBanner() {
  const banner = document.getElementById('push-opt-in-banner');
  if (!banner) return;
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return;

  const vapidPublicKey = banner.dataset.vapidPublicKey;
  const promptText = document.getElementById('push-prompt-text');
  const enabledText = document.getElementById('push-enabled-text');
  const enableBtn = document.getElementById('push-enable-btn');
  const disableBtn = document.getElementById('push-disable-btn');
  const dismissBtn = document.getElementById('push-dismiss-btn');

  const registration = await navigator.serviceWorker.ready;

  function showEnabledState() {
    promptText.style.display = 'none';
    enableBtn.style.display = 'none';
    dismissBtn.style.display = 'none';
    enabledText.style.display = '';
    disableBtn.style.display = '';
    banner.style.display = '';
  }

  function showPromptState() {
    enabledText.style.display = 'none';
    disableBtn.style.display = 'none';
    promptText.style.display = '';
    enableBtn.style.display = '';
    dismissBtn.style.display = '';
    banner.style.display = '';
  }

  const existing = await registration.pushManager.getSubscription();
  if (existing) {
    showEnabledState();
  } else {
    if (localStorage.getItem('push-banner-dismissed') === '1') return;
    if (!vapidPublicKey) return;
    showPromptState();
  }

  enableBtn.addEventListener('click', async () => {
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') { banner.style.display = 'none'; return; }

    let subscription;
    try {
      subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
      });
      const json = subscription.toJSON();
      await postJson('/api/push/subscribe', {
        endpoint: json.endpoint,
        p256dh: json.keys.p256dh,
        auth: json.keys.auth,
      });
    } catch (error) {
      // Keep client/server state consistent: if the server never persisted the
      // subscription, don't leave a browser-side subscription it doesn't know about —
      // otherwise the banner would never reappear (getSubscription() would find it)
      // even though no push will ever be delivered.
      await subscription?.unsubscribe();
      return;
    }
    localStorage.removeItem('push-banner-dismissed');
    showEnabledState();
  });

  disableBtn.addEventListener('click', async () => {
    const subscription = await registration.pushManager.getSubscription();
    if (!subscription) { showPromptState(); return; }

    try {
      // Tell the server first: if that fails, leave the browser subscription and the
      // "enabled" UI intact so the user notices and can retry, instead of silently
      // ending up unsubscribed on the device while the server still has the row.
      await postJson('/api/push/unsubscribe', { endpoint: subscription.endpoint });
      await subscription.unsubscribe();
    } catch (error) {
      return;
    }
    showPromptState();
  });

  dismissBtn.addEventListener('click', () => {
    localStorage.setItem('push-banner-dismissed', '1');
    banner.style.display = 'none';
  });
}

// Self-invoking: reads its config from the banner's own data attribute instead of relying on
// a second <script> tag calling this in the right order. htmx's hx-boost re-injects this file
// on every boosted navigation but doesn't guarantee external <script src> tags finish loading
// before a sibling inline <script> runs — a prior version split these into two tags and hit
// exactly that race ("initPushBanner is not defined").
initPushBanner();

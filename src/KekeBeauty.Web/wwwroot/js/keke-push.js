// Feature 018 (Parcours 3) : abonnement Web Push (VAPID) depuis Blazor. Meme pattern d'interop
// que keke-map.js.
window.kekePush = (function () {
    function urlBase64ToUint8Array(base64String) {
        const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
        const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
        const rawData = window.atob(base64);
        const outputArray = new Uint8Array(rawData.length);
        for (let i = 0; i < rawData.length; i++) {
            outputArray[i] = rawData.charCodeAt(i);
        }
        return outputArray;
    }

    async function isSupported() {
        return 'serviceWorker' in navigator && 'PushManager' in window;
    }

    async function getPermission() {
        return window.Notification ? Notification.permission : 'unsupported';
    }

    async function subscribe(vapidPublicKey) {
        if (!(await isSupported())) {
            return null;
        }

        const permission = await Notification.requestPermission();
        if (permission !== 'granted') {
            return null;
        }

        const registration = await navigator.serviceWorker.ready;
        let subscription = await registration.pushManager.getSubscription();
        if (!subscription) {
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
            });
        }

        const json = subscription.toJSON();
        return {
            endpoint: json.endpoint,
            p256dh: json.keys.p256dh,
            auth: json.keys.auth,
        };
    }

    async function unsubscribe() {
        if (!(await isSupported())) {
            return null;
        }

        const registration = await navigator.serviceWorker.ready;
        const subscription = await registration.pushManager.getSubscription();
        if (!subscription) {
            return null;
        }

        const endpoint = subscription.endpoint;
        await subscription.unsubscribe();
        return endpoint;
    }

    return { isSupported, getPermission, subscribe, unsubscribe };
})();

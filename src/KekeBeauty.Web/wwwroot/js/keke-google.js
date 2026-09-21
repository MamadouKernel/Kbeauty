window.kekeGoogle = (() => {
    let dotnetRef = null;
    let initializedClientId = null;

    async function waitForLibrary() {
        for (let i = 0; i < 50; i++) {
            if (window.google?.accounts?.id) return true;
            await new Promise(resolve => setTimeout(resolve, 100));
        }
        return false;
    }

    async function render(clientId, dotnet, elementId) {
        if (!clientId || !(await waitForLibrary())) return false;
        const host = document.getElementById(elementId);
        if (!host) return false;
        dotnetRef = dotnet;
        if (initializedClientId !== clientId) {
            google.accounts.id.initialize({
                client_id: clientId,
                callback: response => {
                    if (response?.credential && dotnetRef) {
                        dotnetRef.invokeMethodAsync('ReceiveGoogleCredential', response.credential).catch(() => {});
                    }
                },
                auto_select: false,
                cancel_on_tap_outside: true
            });
            initializedClientId = clientId;
        }
        host.replaceChildren();
        google.accounts.id.renderButton(host, {
            type: 'standard', theme: 'outline', size: 'large', text: 'continue_with',
            shape: 'pill', logo_alignment: 'left', width: Math.min(320, Math.max(240, host.clientWidth || 320)), locale: 'fr'
        });
        return true;
    }

    function clear() { dotnetRef = null; }
    function signOut() { window.google?.accounts?.id?.disableAutoSelect(); }
    return { render, clear, signOut };
})();

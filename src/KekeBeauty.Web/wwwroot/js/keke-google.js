window.kekeGoogle = (() => {
    const pendingCredentialKey = 'keke-google-pending-credential';
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
                        // Sur mobile, le navigateur peut suspendre le circuit Blazor pendant
                        // l'ouverture de Google. Conserver brièvement le jeton permet de reprendre
                        // le traitement après reconnexion ou rechargement de la page.
                        try { sessionStorage.setItem(pendingCredentialKey, response.credential); } catch { }
                        dotnetRef.invokeMethodAsync('ReceiveGoogleCredential', response.credential)
                            .then(() => { try { sessionStorage.removeItem(pendingCredentialKey); } catch { } })
                            .catch(() => {});
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

    function takePendingCredential() {
        try {
            const credential = sessionStorage.getItem(pendingCredentialKey);
            if (credential) sessionStorage.removeItem(pendingCredentialKey);
            return credential;
        } catch {
            return null;
        }
    }

    function clear() { dotnetRef = null; }
    function signOut() { window.google?.accounts?.id?.disableAutoSelect(); }
    return { render, takePendingCredential, clear, signOut };
})();

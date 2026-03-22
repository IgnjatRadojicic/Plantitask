window.googleAuth = {
    initialize: function (clientId, dotnetRef) {
        google.accounts.id.initialize({
            client_id: clientId,
            callback: (response) => {
                dotnetRef.invokeMethodAsync('OnGoogleSignIn', response.credential);
            }
        });
    },
    renderButton: function (elementId, options) {
        const element = document.getElementById(elementId);
        if (element) {
            const width = Math.min(element.parentElement.offsetWidth, 400);
            options.width = width;
            google.accounts.id.renderButton(element, options);
        }
    }
};
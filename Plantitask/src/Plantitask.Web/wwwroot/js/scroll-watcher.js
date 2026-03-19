window.scrollWatcher = {
    _handler: null,
    _dotnetRef: null,

    init: function (dotnetRef) {

        this.destroy();

        this._dotnetRef = dotnetRef;
        this._handler = () => {
            dotnetRef.invokeMethodAsync('OnScroll', window.scrollY > 80);
        };
        window.addEventListener('scroll', this._handler, { passive: true });
    },

    destroy: function () {
        if (this._handler) {
            window.removeEventListener('scroll', this._handler);
            this._handler = null;
            this.dotnetRef = null;
        }
    }
};
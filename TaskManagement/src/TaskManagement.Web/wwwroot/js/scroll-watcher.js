window.scrollWatcher = {
    _handler: null,
    _dotnetRef: null,

    init: function (dotnetRef) {
        this._dotnetRef = dotnetRef;
        this._handler = function () {
            var scrolled = window.scrollY > 80;
            dotnetRef.invokeMethodAsync('OnScroll', scrolled);
        };
        window.addEventListener('scroll', this._handler, { passive: true });
    },

    destroy: function () {
        if (this._handler) {
            window.removeEventListener('scroll', this._handler);
            this._handler = null;
        }
    }
};
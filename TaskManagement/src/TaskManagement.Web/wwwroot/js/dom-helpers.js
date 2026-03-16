window.domHelpers = {

    getElementWidth: function (id) {
        return document.getElementById(id)?.offsetWidth ?? window.innerWidth;
    },

    getElementHeight: function (id) {
        return document.getElementById(id)?.offsetHeight ?? window.innerHeight;
    },

    scrollToTop: function () {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
}
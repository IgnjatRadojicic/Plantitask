window.plantitaskLocks = (() => {
    const held = new Map();        

    return {
        acquire: (name) => {
            if (!navigator.locks) return Promise.resolve(false);   // unsupported / insecure context

            return new Promise(acquired => {
                navigator.locks.request(name, () => {
                    // The lock is held for exactly as long as THIS promise stays pending.
                    // We never resolve it here release() does, from the outside.
                    return new Promise(release => {
                        held.set(name, release);
                        acquired(true);        // only now does C# stop awaiting
                    });
                }).catch(() => acquired(false));
            });
        },

        release: (name) => {
            const release = held.get(name);
            if (release) {
                held.delete(name);
                release();                     
            }
        }
    };
})();
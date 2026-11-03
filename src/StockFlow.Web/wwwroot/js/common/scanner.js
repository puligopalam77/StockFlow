window.StockFlowScanner = {
    bind(element, onScan, onError = () => {}) {
        // Callers can supply either an input's ID or the input element itself.
        let input = element;
        if (typeof element === 'string') {
            input = document.getElementById(element);
        }
        if (!input) {
            return;
        }
        // A promise represents work that will finish later. Chaining promises
        // makes each lookup wait for the previous scan instead of running together.
        let queue = Promise.resolve();
        const submit = () => {
            const code = input.value.trim();
            // Clear the box immediately so the scanner can type the next code.
            input.value = '';
            if (!code) {
                return;
            }
            // Queue rapid scans so a slow lookup cannot reorder them or drop input.
            queue = queue
                .then(() => {
                    return onScan(code);
                })
                .catch(onError)
                .finally(() => {
                    if (
                        document.activeElement === input ||
                        document.activeElement === document.body
                    ) {
                        input.focus();
                    }
                });
        };
        input.addEventListener('keydown', (event) => {
            if (event.key === 'Enter' && !event.isComposing) {
                // Scanning should find the item, rather than submit the whole form.
                event.preventDefault();
                if (event.repeat || event.ctrlKey || event.altKey || event.metaKey) return;
                submit();
            }
        });
        // The Look up / Find product button can call the same function.
        window.StockFlowCamera?.attach(input, submit);
        return submit;
    }
};

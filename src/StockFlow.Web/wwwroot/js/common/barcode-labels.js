(() => {
    'use strict';

    const patterns =
        '212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 112232 122132 122231 113222 123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 231113 231311 112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 122411 142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 114131 311141 411131 211412 211214 211232 2331112'.split(
            ' '
        );
    function svg(code) {
        // Set B preserves case and leading zeroes; at least ten modules of quiet zone on both sides.
        if (typeof code !== 'string' || !code.length || !/^[\x20-\x7e]+$/.test(code)) {
            throw new Error(
                'This code cannot be printed as Code 128. Keep its existing label or generate an internal barcode.'
            );
        }
        const values = [...code].map((value) => {
            return value.charCodeAt(0) - 32;
        });
        const checksum =
            (104 +
                values.reduce((sum, value, value3) => {
                    return sum + value * (value3 + 1);
                }, 0)) %
            103;
        const ns = 'http://www.w3.org/2000/svg';
        const node = document.createElementNS(ns, 'svg');
        let value2 = 10;
        for (const value of [104, ...values, checksum, 106]) {
            [...patterns[value]].forEach((width, index) => {
                const w = Number(width);
                if (index % 2 === 0) {
                    const rect = document.createElementNS(ns, 'rect');
                    rect.setAttribute('x', value2);
                    rect.setAttribute('y', 0);
                    rect.setAttribute('width', w);
                    rect.setAttribute('height', 50);
                    rect.setAttribute('fill', '#000');
                    node.append(rect);
                }
                value2 += w;
            });
        }
        // Long codes become too narrow on these labels. Preserve them in storage; offer an internal label instead.
        if (value2 + 10 > 280) {
            throw new Error(
                'This existing code is too long for a 63 mm label. Keep its original label or generate an internal barcode.'
            );
        }
        node.setAttribute('viewBox', `0 0 ${value2 + 10} 50`);
        node.setAttribute('preserveAspectRatio', 'none');
        node.setAttribute('role', 'img');
        node.setAttribute('aria-label', 'Barcode ' + code);
        return node;
    }
    window.StockFlowBarcode = {
        svg
    };
})();

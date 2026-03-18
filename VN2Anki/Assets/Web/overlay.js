document.addEventListener('mousedown', (e) => {
    let textBox = document.getElementById('text-box');
    let insideTextBox = false;
    if (textBox) {
        let r = textBox.getBoundingClientRect();
        insideTextBox = e.clientX >= r.left && e.clientX <= r.right &&
                        e.clientY >= r.top  && e.clientY <= r.bottom;
    }

    if (!insideTextBox) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(JSON.stringify({
                forwardClick: true,
                x: e.screenX,
                y: e.screenY,
                button: e.button
            }));
        }
        e.preventDefault();
        e.stopPropagation();
    }
});

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', event => {
        let msg = event.data;
        // Se a mensagem vier como string, faz o parse (depende de como C# envia)
        if (typeof msg === 'string') {
            try { msg = JSON.parse(msg); } catch (e) { return; }
        }

        if (msg.action === 'newText') {
            const container = document.getElementById('text-box');
            if(container) {
                container.innerHTML = '';
                const newSpan = document.createElement('span');
                newSpan.className = 'vn-text-line';
                newSpan.innerHTML = msg.data.text;
                container.appendChild(newSpan);
            }
        } else if (msg.action === 'updateStyle') {
            applyStyles(msg.data);
        } else if (msg.action === 'updatePosition') {
            applyPosition(msg.data);
        } else if (msg.action === 'updateTransparency') {
            document.body.className = msg.data.isTransparent ? 'transp-on' : 'transp-on transp-off';
        }
    });
}

function applyStyles(data) {
    let style = document.getElementById('dynamic-config-style');
    if (!style) {
        style = document.createElement('style');
        style.id = 'dynamic-config-style';
        document.head.appendChild(style);
    }
    
    // box styles
    let boxStyles = "";
    if (data.useTextBoxMode) {
        let align = data.textVerticalAlignment || "center";
        boxStyles = `
            min-height: ${data.textBoxMinHeight}px;
            width: ${data.textBoxWidthPercentage}vw;
            display: flex;
            flex-direction: column;
            justify-content: ${align};
            align-items: center; /* Centraliza horizontalmente o texto dentro da caixa */
            box-sizing: border-box;
            margin-left: auto;
            margin-right: auto;
        `;
    } else {
        boxStyles = `
            width: auto;
            min-height: auto;
            display: inline-block;
            margin-left: auto;
            margin-right: auto;
        `;
    }

    // outline styles
    let textOutline = "";
    if (data.outlineThickness > 0) {
        let t = data.outlineThickness;
        let c = data.outlineColor;
        let shadowParts = [];
        for (let x = -t; x <= t; x++) {
            for (let y = -t; y <= t; y++) {
                if (x === 0 && y === 0) continue;
                shadowParts.push(`${x}px ${y}px 0px ${c}`);
            }
        }
        shadowParts.push(`0px 0px ${t}px ${c}`);
        textOutline = `text-shadow: ${shadowParts.join(', ')} !important;\n                -webkit-text-stroke: 0 !important;`;
    } else {
        textOutline = `text-shadow: none !important; -webkit-text-stroke: 0 !important;`;
    }

    style.innerHTML = `
        html, body { width: 100vw; }
        #text-box {
            color: ${data.fontColor} !important; 
            font-family: '${data.fontFamily}', sans-serif !important;
            font-size: ${data.fontSize}px !important;
            background-color: ${data.bgColor} !important;
            border-radius: 8px;
            padding: 15px;
            text-align: center;
            ${textOutline}
            ${boxStyles}
        }
        /* Regra para o botão do 'olhinho' (Transparência) funcionar só na caixa de texto */
        body.transp-on:not(.transp-off) #text-box {
            background-color: transparent !important;
            box-shadow: none !important;
        }
    `;
}

function applyPosition(data) {
    document.body.style.justifyContent = data.isTextAtTop ? "flex-start" : "flex-end";
    const tb = document.getElementById('text-box');
    if (tb) {
        let margin = data.isTextAtTop
            ? `margin-top: ${data.verticalMargin}px; margin-bottom: 0px;`
            : `margin-bottom: ${data.verticalMargin}px; margin-top: 0px;`;
        let transform = `transform: translateX(${data.horizontalDisplacement}px);`;
        tb.style.cssText = `${margin} ${transform}`;
    }
}
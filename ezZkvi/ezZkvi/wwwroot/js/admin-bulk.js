(() => {
    const forms = [...document.querySelectorAll('[data-bulk-kind]')];
    const visible = check => getComputedStyle(check.closest('.t-row,.feed-card')).display !== 'none';
    function checks(form) { return [...document.querySelectorAll(`.bulk-check[data-kind="${form.dataset.bulkKind}"]`)]; }
    function update() {
        forms.forEach(form => {
            const list = checks(form);
            list.filter(c => !visible(c)).forEach(c => c.checked = false);
            const shown = list.filter(visible), selected = shown.filter(c => c.checked);
            form.querySelector('.bulk-count').textContent = `Odabrano: ${selected.length}`;
            form.querySelector('button[type="submit"]').disabled = !selected.length;
            const all = form.querySelector('.bulk-all');
            all.checked = shown.length > 0 && selected.length === shown.length;
            all.indeterminate = selected.length > 0 && selected.length < shown.length;
        });
    }
    document.addEventListener('change', update);
    let scheduled = false;
    document.addEventListener('bulk-filter-changed', () => {
        if (scheduled) return;
        scheduled = true;
        queueMicrotask(() => { scheduled = false; update(); });
    });
    forms.forEach(form => {
        form.querySelector('.bulk-all').addEventListener('change', e => {
            checks(form).filter(visible).forEach(c => c.checked = e.target.checked);
            update();
        });
        form.addEventListener('submit', async e => {
            e.preventDefault();
            const selected = checks(form).filter(c => visible(c) && c.checked);
            if (!selected.length) return;
            const button = form.querySelector('button[type="submit"]');
            button.disabled = true;
            try {
                const data = new FormData(form);
                selected.forEach(c => data.append('ids', c.value));
                let message = `Jeste li sigurni da želite trajno obrisati ${selected.length} odabranih stavki?`;
                if (form.dataset.bulkKind !== 'feedback') {
                    const response = await fetch('/Content/DeletionPreview', { method: 'POST', body: data });
                    if (!response.ok) throw new Error();
                    const counts = await response.json();
                    message += `\nBriše se pitanja: ${counts.questions}, oblasti: ${counts.areas}, kviz sesija: ${counts.sessions}, feedbackova: ${counts.feedback}.`;
                    if (form.dataset.bulkKind === 'subjects') message += '\nBriše se i sva zbirna statistika odabranih predmeta.';
                    if (form.dataset.bulkKind === 'questions') message += '\nBrišu se ponuđeni odgovori i povezane stavke kviz sesija.';
                }
                if (!confirm(message)) return;
                form.querySelectorAll('input[name="ids"]').forEach(i => i.remove());
                selected.forEach(c => { const input = document.createElement('input'); input.type='hidden'; input.name='ids'; input.value=c.value; form.append(input); });
                HTMLFormElement.prototype.submit.call(form);
            } catch { alert('Nije moguće provjeriti brisanje. Osvježite stranicu i pokušajte ponovo.'); }
            finally { update(); }
        });
    });
    // Existing feedback filters only toggle CSS; keep selection restricted to visible rows.
    document.querySelectorAll('.feed-card').forEach(row => new MutationObserver(update).observe(row, { attributes: true, attributeFilter: ['style'] }));
    update();
})();

// Row expand/collapse is a client-side class toggle; site.js keeps the class across SSE morphs.
window.householdApp ??= {};

if (!window.householdApp.expensesEventsBound) {
    document.addEventListener('click', (e) => {
        const header = e.target.closest('.expense-row-header');
        if (!header) return;
        header.closest('.expense-row-wrap')?.classList.toggle('expanded');
    });

    window.householdApp.expensesEventsBound = true;
}

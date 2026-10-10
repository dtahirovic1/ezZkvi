// Shared mobile navigation. Does not modify quiz, forms or existing Ajax flows.
(() => {
  'use strict';
  const shell = document.querySelector('.ez-app-frame');
  if (!shell) return;
  const trigger = shell.querySelector('[data-ez-sidebar-open]');
  const closeButtons = shell.querySelectorAll('[data-ez-sidebar-close]');
  const sidebar = shell.querySelector('#ezSidebar');
  function setOpen(open) {
    shell.classList.toggle('ez-nav-open', open);
    document.body.classList.toggle('ez-mobile-nav-open', open);
    trigger?.setAttribute('aria-expanded', String(open));
    if (open) sidebar?.querySelector('.ez-sidebar-close')?.focus();
    else if (sidebar?.contains(document.activeElement)) trigger?.focus();
  }
  trigger?.addEventListener('click', () => setOpen(!shell.classList.contains('ez-nav-open')));
  closeButtons.forEach(element => element.addEventListener('click', () => setOpen(false)));
  shell.querySelectorAll('.ez-sidebar-nav a').forEach(a => a.addEventListener('click', () => setOpen(false)));
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && shell.classList.contains('ez-nav-open')) setOpen(false); });
  const mq = window.matchMedia('(min-width: 1025px)');
  mq.addEventListener?.('change', e => { if (e.matches) setOpen(false); });
  // Gentle reveal for dashboard tiles; do not hide elements if scripting is disabled.
  const motion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (!motion && 'IntersectionObserver' in window) {
    const cards = Array.from(shell.querySelectorAll('.stat-card, .stats > .stat, .section-card')).slice(0, 20);
    const watcher = new IntersectionObserver(entries => entries.forEach(({target, isIntersecting}) => {
      if (isIntersecting) { target.classList.add('ez-appeared'); watcher.unobserve(target); }
    }), {threshold: .06});
    cards.forEach((card, index) => { card.classList.add('ez-enter'); card.style.setProperty('--ez-delay', `${Math.min(index * 28, 190)}ms`); watcher.observe(card); });
  }
})();

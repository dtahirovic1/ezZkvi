/* UI-only enhancements. Server actions, navigation and quiz scoring are unchanged. */
(() => {
  'use strict';
  const ready = (fn) => document.readyState === 'loading'
    ? document.addEventListener('DOMContentLoaded', fn, { once: true }) : fn();

  ready(() => {
    // An interactive sample question on the landing page (not a database quiz).
    const answers = Array.from(document.querySelectorAll('[data-demo-answer]'));
    const feedback = document.getElementById('showcaseFeedback');
    const reset = document.getElementById('showcaseReset');
    if (answers.length && feedback && reset) {
      const initialText = feedback.textContent;
      answers.forEach((answer) => answer.addEventListener('click', () => {
        if (answers.some((item) => item.disabled)) return;
        const correct = answer.dataset.demoAnswer === 'true';
        answers.forEach((item) => {
          item.disabled = true;
          item.setAttribute('aria-pressed', String(item === answer));
          if (item.dataset.demoAnswer === 'true') item.classList.add('is-correct');
        });
        if (!correct) answer.classList.add('is-wrong');
        feedback.textContent = correct
          ? 'Tačno. Skup sa 4 elementa ima 2⁴ = 16 podskupova.'
          : 'Nije tačno. Broj podskupova je 2⁴ = 16.';
        feedback.classList.add(correct ? 'is-correct' : 'is-wrong');
        reset.hidden = false;
      }));
      reset.addEventListener('click', () => {
        answers.forEach((answer) => {
          answer.disabled = false;
          answer.classList.remove('is-correct', 'is-wrong');
          answer.setAttribute('aria-pressed', 'false');
        });
        feedback.textContent = initialText;
        feedback.classList.remove('is-correct', 'is-wrong');
        reset.hidden = true;
      });
    }

    // Fade-in sections only when IntersectionObserver is available.
    const sectionTargets = document.querySelectorAll('.rc-animate-in');
    if (sectionTargets.length && 'IntersectionObserver' in window && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      document.body.classList.add('rc-js-ready');
      const observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) { entry.target.classList.add('rc-visible'); observer.unobserve(entry.target); }
        }
      }, { rootMargin: '0px 0px 60px 0px', threshold: .08 });
      sectionTargets.forEach(el => observer.observe(el));
    }

  });
})();

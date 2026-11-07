import './styles/app.css';

const root = document.getElementById('app');
if (root) {
  root.innerHTML = `
    <div class="mob-head">
      <div><strong>بوابة المندوب</strong><div class="muted small">كشوف الحساب · المديونيات · الإشعارات</div></div>
      <span class="tag good">جاهز</span>
    </div>
    <div class="mob-card">
      <p>مرحباً بك في تطبيق مندوب البوابة.</p>
    </div>
  `;
}

const { icons, settings } = globalThis.FlyyLink;
const saved = document.getElementById('saved');
let savedTimer = null;

document.querySelectorAll('[data-icon]').forEach((element) => (element.innerHTML = icons[element.dataset.icon]));

settings.load().then((values) => {
  for (const field of document.querySelectorAll('[name]')) {
    if (field.type === 'checkbox') field.checked = values[field.name];
    else field.value = values[field.name];

    field.addEventListener('change', async () => {
      await settings.save({ [field.name]: field.type === 'checkbox' ? field.checked : field.value });
      clearTimeout(savedTimer);
      saved.hidden = false;
      savedTimer = setTimeout(() => (saved.hidden = true), 1500);
    });
  }
});

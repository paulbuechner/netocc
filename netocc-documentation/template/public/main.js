// Diagrams open in a lightbox on the page, not in a new tab: DocFX wraps every larger article image in a link to it
// unless showLightbox returns false for it. A native <dialog> brings the backdrop, Esc and focus return; a click
// anywhere closes it, Enter or Space on a focused diagram opens it.
const diagrams = 'img.diagram-light, img.diagram-dark'

let lightbox

function open(img) {
  if (!lightbox) {
    lightbox = document.createElement('dialog')
    lightbox.className = 'diagram-lightbox'
    lightbox.append(document.createElement('img'))
    lightbox.addEventListener('click', () => lightbox.close())
    document.body.append(lightbox)
  }
  const big = lightbox.querySelector('img')
  big.src = img.currentSrc || img.src
  big.alt = img.alt
  lightbox.showModal()
}

for (const img of document.querySelectorAll(diagrams)) {
  img.tabIndex = 0
  img.setAttribute('role', 'button')
  img.setAttribute('aria-haspopup', 'dialog')
  img.addEventListener('click', () => open(img))
  img.addEventListener('keydown', e => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      open(img)
    }
  })
}

export default {
  showLightbox: img => !img.matches(diagrams),
}

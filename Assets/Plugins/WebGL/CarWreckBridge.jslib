// Lets the page read the game's state (window.carwreckState) for diagnostics and automated checks.
mergeInto(LibraryManager.library, {
  CarWreck_Report: function (ptr) {
    try { window.carwreckState = JSON.parse(UTF8ToString(ptr)); } catch (e) { }
  }
});

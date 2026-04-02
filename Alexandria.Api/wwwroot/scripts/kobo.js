// ===== API Helpers =====
function coverUrl(uuid) {
    return "/api/alexandria/books/" + uuid + "/cover";
}

function formatFileSize(bytes) {
    if (!bytes) { return ""; }
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
}

function readingStatusText(progress) {
    if (!progress || progress.status === "ReadyToRead") { return "Unread"; }
    if (progress.progressPercent != null) { return Math.round(progress.progressPercent) + "% Read"; }
    return progress.status;
}

function fallbackColor(uuid) {
    var hash = 0;
    for (var i = 0; i < uuid.length; i++) {
        hash = uuid.charCodeAt(i) + ((hash << 5) - hash);
    }
    var h = Math.abs(hash) % 360;
    return "hsl(" + h + ", 30%, 35%)";
}

function bookCoverHtml(book, cssClass) {
    var cls = cssClass || "book-cover-img";
    return '<img src="' + coverUrl(book.uuid) + '" class="' + cls + '" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' +
           '<div class="book-cover" style="display:none;background:' + fallbackColor(book.uuid) + ';">' + escapeHtml(book.title) + '<small>' + escapeHtml((book.authors || []).join(", ")) + '</small></div>';
}

function escapeHtml(text) {
    if (!text) { return ""; }
    return $("<span>").text(text).html();
}

// ===== Sort/Filter Persistence =====
function getSortPrefs(pageKey, defaultSort, defaultOrder) {
    return {
        sort: localStorage.getItem(pageKey + ".sort") || defaultSort,
        order: localStorage.getItem(pageKey + ".order") || defaultOrder
    };
}

function saveSortPrefs(pageKey, sort, order) {
    localStorage.setItem(pageKey + ".sort", sort);
    localStorage.setItem(pageKey + ".order", order);
}

function getFilterPref(pageKey, defaultFilter) {
    return localStorage.getItem(pageKey + ".filter") || defaultFilter;
}

function saveFilterPref(pageKey, filter) {
    localStorage.setItem(pageKey + ".filter", filter);
}

function sortArrow(order) {
    return order === "Desc" ? " \u25BC" : " \u25B2";
}

// Restore a sort dropdown UI from saved prefs
function restoreSortDropdown($dropdown, sort, order) {
    var $toggle = $dropdown.find(".dropdown-toggle");
    var $items = $dropdown.find(".dropdown-item[data-sort]");
    $items.find(".check").text("");
    var $active = $items.filter("[data-sort='" + sort + "']");
    if ($active.length) {
        $active.find(".check").text("\u2713");
        var label = $toggle.data("label") || "";
        $toggle.text(label + $active.data("value") + sortArrow(order));
    }
}

// Restore a filter dropdown UI from saved pref
function restoreFilterDropdown($dropdown, filter) {
    var $toggle = $dropdown.find(".dropdown-toggle");
    var $items = $dropdown.find(".dropdown-item[data-filter]");
    $items.find(".check").text("");
    var $active = $items.filter("[data-filter='" + filter + "']");
    if ($active.length) {
        $active.find(".check").text("\u2713");
        var label = $toggle.data("label") || "";
        $toggle.text(label + $active.data("value"));
    }
}

// Global page reload callback — each page sets this
var pageReload = null;

$(function () {
    // ===== Clock =====
    function updateClock() {
        var now = new Date();
        var hh = String(now.getHours()).padStart(2, "0");
        var mm = String(now.getMinutes()).padStart(2, "0");
        $("#clock").text(hh + ":" + mm);
    }
    updateClock();
    setInterval(updateClock, 60000);

    // ===== Dark Mode Toggle =====
    var savedTheme = localStorage.getItem("theme") || "light";
    $("html").attr("data-bs-theme", savedTheme);
    updateThemeIcon(savedTheme);

    $("#theme-toggle").on("click", function () {
        var current = $("html").attr("data-bs-theme");
        var next = current === "dark" ? "light" : "dark";
        $("html").attr("data-bs-theme", next);
        localStorage.setItem("theme", next);
        updateThemeIcon(next);
    });

    function updateThemeIcon(theme) {
        if (theme === "dark") {
            $("#theme-icon-sun").addClass("d-none");
            $("#theme-icon-moon").removeClass("d-none");
        } else {
            $("#theme-icon-sun").removeClass("d-none");
            $("#theme-icon-moon").addClass("d-none");
        }
    }

    // ===== Sort Dropdown Selection =====
    $(document).on("click", ".dropdown-item[data-sort]", function (e) {
        e.preventDefault();
        var $item = $(this);
        var $dropdown = $item.closest(".dropdown");
        var $toggle = $dropdown.find(".dropdown-toggle");
        var pageKey = $dropdown.data("page-key");
        var newSort = $item.data("sort");
        var prefs = getSortPrefs(pageKey, newSort, $item.data("default-order") || "asc");

        var newOrder;
        if (prefs.sort === newSort) {
            newOrder = prefs.order === "Asc" ? "Desc" : "Asc";
        } else {
            newOrder = $item.data("default-order") || "Asc";
        }

        saveSortPrefs(pageKey, newSort, newOrder);

        $dropdown.find(".check").text("");
        $item.find(".check").text("\u2713");
        var label = $toggle.data("label") || "";
        $toggle.text(label + $item.data("value") + sortArrow(newOrder));

        if (pageReload) { pageReload(); }
    });

    // ===== Filter Dropdown Selection =====
    $(document).on("click", ".dropdown-item[data-filter]", function (e) {
        e.preventDefault();
        var $item = $(this);
        var $dropdown = $item.closest(".dropdown");
        var $toggle = $dropdown.find(".dropdown-toggle");
        var pageKey = $dropdown.data("page-key");

        saveFilterPref(pageKey, $item.data("filter"));

        $dropdown.find(".check").text("");
        $item.find(".check").text("\u2713");
        var label = $toggle.data("label") || "";
        $toggle.text(label + $item.data("value"));

        if (pageReload) { pageReload(); }
    });

    // ===== View Toggle (Grid / List) =====
    $("[data-view-toggle]").on("click", function (e) {
        e.preventDefault();
        var view = $(this).data("view-toggle");
        var $grid = $(".view-grid-container");
        var $list = $(".view-list-container");

        if ($grid.length && $list.length) {
            if (view === "grid") {
                $grid.removeClass("d-none");
                $list.addClass("d-none");
            } else {
                $grid.addClass("d-none");
                $list.removeClass("d-none");
            }
        }
    });
});

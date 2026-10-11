// ===== Sort/Filter Persistence =====
var pageReload = null;

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

function restoreSortDropdown($dropdown, sort, order) {
    var $toggle = $dropdown.find(".dropdown-toggle");
    var $items = $dropdown.find(".dropdown-item[data-sort]");
    $items.find(".check").text("");
    $items.find(".sort-arrow").text("");
    var $active = $items.filter("[data-sort='" + sort + "']");
    if ($active.length) {
        $active.find(".check").text("\u2713");
        $active.find(".sort-arrow").text(sortArrow(order));
        var label = $toggle.data("label") || "";
        $toggle.text(label + $active.data("value"));
    }
}

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

function updateMediaTypeFilters($dropdown, books) {
    var present = {};
    books.forEach(function (b) { present[b.file.mediaType] = true; });
    var showFilters = Object.keys(present).length > 1;
    $dropdown.find("[data-filter='ebooks']").closest("li").toggle(showFilters && !!present.Ebook);
    $dropdown.find("[data-filter='audiobooks']").closest("li").toggle(showFilters && !!present.Audiobook);
    $dropdown.find("[data-filter='comics']").closest("li").toggle(showFilters && !!present.Comic);
}

function applyMediaTypeFilter(books, filter) {
    if (filter === "ebooks")
        return books.filter(function (b) { return b.file.mediaType === "Ebook"; });
    if (filter === "audiobooks")
        return books.filter(function (b) { return b.file.mediaType === "Audiobook"; });
    if (filter === "comics")
        return books.filter(function (b) { return b.file.mediaType === "Comic"; });
    return books;
}

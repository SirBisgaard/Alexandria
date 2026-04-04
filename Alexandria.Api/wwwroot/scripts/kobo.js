// ===== API Helpers =====
var COVER_LARGE = { w: 400, h: 600 };
var COVER_SMALL = { w: 120, h: 180 };

function coverUrl(uuid, width, height) {
    var url = "/api/alexandria/books/" + uuid + "/cover";
    if (width && height) { url += "?width=" + width + "&height=" + height; }
    return url;
}

function formatFileSize(bytes) {
    if (!bytes) { return ""; }
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
}

function readingStatusText(progress) {
    if (!progress || progress.status === "ReadyToRead") { return "Unread"; }
    if (progress.status === "Finished" || (progress.progressPercent != null && Math.round(progress.progressPercent) >= 100)) { return "Finished"; }
    if (progress.progressPercent != null) { return Math.round(progress.progressPercent) + "% Read"; }
    return progress.status;
}

function readingStatusRaw(progress) {
    if (!progress || progress.status === "ReadyToRead") { return "ReadyToRead"; }
    return progress.status;
}

function statusMenuItems(uuid, rawStatus) {
    var items = "";
    if (rawStatus !== "Finished") {
        items += '<li><a class="dropdown-item" href="#" data-mark-finished="' + uuid + '">Mark as finished</a></li>';
    }
    if (rawStatus === "Reading" || rawStatus === "Finished") {
        items += '<li><a class="dropdown-item" href="#" data-mark-unread="' + uuid + '">Mark as unread</a></li>';
    }
    return items;
}

function updateReadingStatus(uuid, status) {
    $.ajax({ url: "/api/alexandria/books/" + uuid + "/status", method: "PUT", contentType: "application/json", data: JSON.stringify({ status: status }), success: function () { if (pageReload) { pageReload(); } if (currentModalUuid === uuid) { openBookModal(uuid); } } });
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
    var size = cls === "book-list-cover-img" ? COVER_SMALL : COVER_LARGE;
    return '<div class="cover-frame ' + cls + '"><img loading="lazy" src="' + coverUrl(book.uuid, size.w, size.h) + '" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' +
           '<div class="book-cover" style="display:none;background:' + fallbackColor(book.uuid) + ';">' + escapeHtml(book.title) + '<small>' + escapeHtml((book.authors || []).join(", ")) + '</small></div></div>';
}

function homeStackCoverHtml(book) {
    return '<div class="home-cover-slot">' + bookCoverHtml(book) + '</div>';
}

function homeSingleCoverHtml(book) {
    return '<div class="home-cover-single-slot">' + bookCoverHtml(book) + '</div>';
}

function escapeHtml(text) {
    if (!text) { return ""; }
    return $("<span>").text(text).html();
}

function highlightJson(obj) {
    var raw = JSON.stringify(obj, null, 2);
    var escaped = raw.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    return escaped.replace(/("(?:\\.|[^"\\])*")\s*:|"(?:\\.|[^"\\])*"|\b(-?\d+\.?\d*(?:[eE][+-]?\d+)?)\b|\b(true|false)\b|\bnull\b/g, function (match, key, num, bool) { if (key) { return "<span class='json-key'>" + key + "</span>:"; } if (match[0] === '"') { return "<span class='json-string'>" + match + "</span>"; } if (num) { return "<span class='json-number'>" + match + "</span>"; } if (bool) { return "<span class='json-bool'>" + match + "</span>"; } return "<span class='json-null'>" + match + "</span>"; });
}

// ===== Shared renderBooks =====
function renderBooks(books) {
    var $grid = $("#book-grid").empty();
    var $list = $("#book-list").empty();
    $.each(books, function (i, book) {
        var status = readingStatusText(book.readingProgress);
        var rawStatus = readingStatusRaw(book.readingProgress);
        var authors = (book.authors || []).join(", ");
        var series = book.seriesName ? (book.seriesName.toUpperCase() + (book.seriesIndex ? " - " + book.seriesIndex : "")) : "";
        var extraItems = statusMenuItems(book.uuid, rawStatus);

        $grid.append(
            '<div class="book-card" data-book-uuid="' + book.uuid + '">' +
                bookCoverHtml(book, "book-cover-img") +
                '<div class="book-status">' + escapeHtml(status) + '</div>' +
                '<div class="dropdown book-menu-dropdown">' +
                    '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                    '<ul class="dropdown-menu dropdown-menu-end">' +
                        '<li><a class="dropdown-item" href="#" data-view-details="' + book.uuid + '">View Details</a></li>' +
                        extraItems +
                    '</ul>' +
                '</div>' +
            '</div>'
        );

        $list.append(
            '<div class="book-list-item" data-book-uuid="' + book.uuid + '">' +
                bookCoverHtml(book, "book-list-cover-img") +
                '<div class="book-list-info">' +
                    '<div class="book-list-title">' + escapeHtml(book.title) + '</div>' +
                    '<div class="book-list-series">' + escapeHtml(series) + '</div>' +
                    '<div class="book-list-author">' + escapeHtml(authors) + '</div>' +
                '</div>' +
                '<div class="book-list-right">' +
                    '<div class="book-list-read-status">' + escapeHtml(status) + '</div>' +
                    (book.readingProgress && book.readingProgress.remainingTimeMinutes ? '<div class="book-list-time-remaining">' + Math.round(book.readingProgress.remainingTimeMinutes / 60) + ' hours to go</div>' : '') +
                    '<div class="book-list-format">KEPUB - ' + formatFileSize(book.kepubFileSize) + '</div>' +
                    '<div class="dropdown book-menu-dropdown">' +
                        '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                        '<ul class="dropdown-menu dropdown-menu-end">' +
                            '<li><a class="dropdown-item" href="#" data-view-details="' + book.uuid + '">View Details</a></li>' +
                            extraItems +
                        '</ul>' +
                    '</div>' +
                '</div>' +
            '</div>'
        );
    });
}

// ===== Book Detail Modal =====
var currentModalUuid = null;

function openBookModal(uuid) {
    $.get("/api/alexandria/books/" + uuid, function (book) {
        currentModalUuid = book.uuid;
        var authors = (book.authors || []).join(", ");
        var series = book.seriesName ? (book.seriesName.toUpperCase() + (book.seriesIndex ? " - " + book.seriesIndex : "")) : "";
        var status = readingStatusText(book.readingProgress);
        var rawStatus = readingStatusRaw(book.readingProgress);

        $("#bm-cover").attr("src", coverUrl(book.uuid, COVER_LARGE.w, COVER_LARGE.h)).show();
        $("#bm-cover-frame").show();
        $("#bm-cover-fallback").hide().css("background", fallbackColor(book.uuid)).html(escapeHtml(book.title) + '<small>' + escapeHtml(authors) + '</small>');
        $("#bm-title").text(book.title);
        $("#bm-series").text(series).toggle(!!series);
        $("#bm-author").text(authors);
        var modalStatusItems = statusMenuItems(book.uuid, rawStatus);
        $("#bm-status").text(status);
        if (modalStatusItems) {
            $("#bm-status-menu").html('<button class="bm-status-menu-btn" data-bs-toggle="dropdown">&middot;&middot;&middot;</button><ul class="dropdown-menu dropdown-menu-end">' + modalStatusItems + '</ul>').show();
        } else {
            $("#bm-status-menu").hide();
        }

        // Synopsis
        var desc = book.description || "No description available.";
        $("#bm-synopsis").html(desc);

        // Details
        var details = "";
        if (book.publisher) { details += "<div class='detail-row'>Publisher: " + escapeHtml(book.publisher) + (book.publicationDate ? " (" + escapeHtml(book.publicationDate) + ")" : "") + "</div>"; }
        if (book.language) { details += "<div class='detail-row'>Language: " + escapeHtml(book.language) + "</div>"; }
        if (book.kepubFileSize) { details += "<div class='detail-row'>File Size: " + formatFileSize(book.kepubFileSize) + "</div>"; }
        details += "<div class='detail-row'>File Format: KEPUB</div>";
        $("#bm-details").html(details || "No details available.");

        // Data
        $("#bm-data-code").html(highlightJson(book));

        // Show synopsis tab by default
        $("#bm-tab-synopsis").addClass("active");
        $("#bm-tab-details").removeClass("active");
        $("#bm-tab-data").removeClass("active");
        $("#bm-synopsis").show();
        $("#bm-details").hide();
        $("#bm-data").hide();

        $("#book-modal-backdrop").addClass("open");
    });
}

function closeBookModal() {
    $("#book-modal-backdrop").removeClass("open");
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

var pageReload = null;

$(function () {
    // ===== Inject sort arrow spans into sort dropdown items =====
    $(".dropdown-item[data-sort]").append(' <span class="sort-arrow"></span>');

    // ===== Inject Modal HTML =====
    $("body").append(
        '<div class="book-modal-backdrop" id="book-modal-backdrop">' +
            '<div class="book-modal" id="book-modal">' +
                '<div class="book-modal-title-bar">My Books</div>' +
                '<button class="book-modal-close" id="bm-close">&times;</button>' +
                '<div class="book-modal-info">' +
                    '<div class="cover-frame book-modal-cover" id="bm-cover-frame"><img id="bm-cover" src="" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' +
                    '<div id="bm-cover-fallback" class="book-cover" style="display:none;"></div></div>' +
                    '<div class="book-modal-meta">' +
                        '<div class="book-modal-book-title" id="bm-title"></div>' +
                        '<div class="book-modal-series" id="bm-series"></div>' +
                        '<div class="book-modal-author" id="bm-author"></div>' +
                        '<div class="book-modal-status-row"><div class="book-modal-status" id="bm-status"></div><div class="dropdown" id="bm-status-menu"></div></div>' +
                    '</div>' +
                '</div>' +
                '<ul class="nav nav-underline book-modal-tabs">' +
                    '<li class="nav-item"><a class="nav-link active" id="bm-tab-synopsis" href="#">Synopsis</a></li>' +
                    '<li class="nav-item"><a class="nav-link" id="bm-tab-details" href="#">Details</a></li>' +
                    '<li class="nav-item ms-auto"><a class="nav-link" id="bm-tab-data" href="#">Data</a></li>' +
                '</ul>' +
                '<div class="book-modal-tab-content">' +
                    '<div id="bm-synopsis"></div>' +
                    '<div id="bm-details" style="display:none;"></div>' +
                    '<div id="bm-data" style="display:none;"><p class="bm-data-info">Raw book metadata as returned by the Alexandria API.</p><pre><code id="bm-data-code"></code></pre></div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    // ===== Inject Search Modal HTML =====
    $("body").append(
        '<div class="search-modal-backdrop" id="search-modal-backdrop">' +
            '<div class="search-modal" id="search-modal">' +
                '<div class="search-modal-title-bar">Search</div>' +
                '<button class="search-modal-close" id="search-close">&times;</button>' +
                '<div class="search-input-wrap">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>' +
                    '<input type="text" class="search-input" id="search-input" placeholder="Search books..." autocomplete="off">' +
                '</div>' +
                '<div id="search-results"></div>' +
            '</div>' +
        '</div>'
    );

    // ===== Inject About Modal HTML =====
    $("body").append(
        '<div class="book-modal-backdrop" id="about-modal-backdrop">' +
            '<div class="book-modal" id="about-modal">' +
                '<div class="book-modal-title-bar">About Alexandria</div>' +
                '<button class="book-modal-close" id="about-close">&times;</button>' +
                '<div class="about-modal-content">' +
                    '<p>Named after the ancient Library of Alexandria, Alexandria is an offline e-book library manager designed for Kobo e-readers.</p>' +
                    '<p>Set out to enable offline use of your devices, it works by acting as a transparent proxy between your Kobo and the Kobo store, intercepting sync endpoints to seamlessly inject your personal library into the standard Kobo reading experience.</p>' +
                    '<p>Your books appear alongside store purchases on your device, complete with covers, reading progress, and metadata &mdash; no sideloading or USB transfers required.</p>' +
                    '<p>Simply point your Kobo at Alexandria, drop EPUB files into the library folder, and your books will sync automatically.</p>' +
                    '<div class="about-logo">Alexandria</div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    $(document).on("click", "[data-about-alexandria]", function (e) {
        e.preventDefault();
        $("#about-modal-backdrop").addClass("open");
    });

    $("#about-close").on("click", function () {
        $("#about-modal-backdrop").removeClass("open");
    });

    $("#about-modal-backdrop").on("click", function (e) {
        if (e.target === this) { $("#about-modal-backdrop").removeClass("open"); }
    });

    // ===== Inject Settings Modal HTML =====
    $("body").append(
        '<div class="book-modal-backdrop" id="settings-modal-backdrop">' +
            '<div class="book-modal" id="settings-modal">' +
                '<div class="book-modal-title-bar">Settings</div>' +
                '<button class="book-modal-close" id="settings-close">&times;</button>' +
                '<div class="settings-modal-content">' +
                    '<div class="list-group list-group-flush">' +
                        '<a href="#" class="list-group-item list-group-item-action px-0">Library path</a>' +
                        '<a href="#" class="list-group-item list-group-item-action px-0">Syncing and updates</a>' +
                        '<a href="#" class="list-group-item list-group-item-action px-0">Proxy settings</a>' +
                        '<a href="#" class="list-group-item list-group-item-action px-0">Sync batch size</a>' +
                        '<a href="#" class="list-group-item list-group-item-action px-0">Traffic logging</a>' +
                        '<a href="#" class="list-group-item list-group-item-action px-0">Device information</a>' +
                    '</div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    $(document).on("click", "[data-open-settings]", function (e) {
        e.preventDefault();
        $("#settings-modal-backdrop").addClass("open");
    });

    $("#settings-close").on("click", function () {
        $("#settings-modal-backdrop").removeClass("open");
    });

    $("#settings-modal-backdrop").on("click", function (e) {
        if (e.target === this) { $("#settings-modal-backdrop").removeClass("open"); }
    });

    // ===== Search Modal Open/Close =====
    var searchCache = null;
    var searchTimer = null;

    function openSearchModal() {
        $("#search-input").val("");
        $("#search-results").empty();
        searchCache = null;
        $("#search-modal-backdrop").addClass("open");
        setTimeout(function () { $("#search-input").focus(); }, 50);
    }

    function closeSearchModal() {
        $("#search-modal-backdrop").removeClass("open");
    }

    $("#search-btn").on("click", openSearchModal);
    $("#search-close").on("click", closeSearchModal);
    $("#search-modal-backdrop").on("click", function (e) {
        if (e.target === this) { closeSearchModal(); }
    });

    $(document).on("keydown", function (e) {
        if (e.key === "Escape" && $("#search-modal-backdrop").hasClass("open")) { closeSearchModal(); }
        if (e.key === "Escape" && $("#about-modal-backdrop").hasClass("open")) { $("#about-modal-backdrop").removeClass("open"); }
        if (e.key === "Escape" && $("#settings-modal-backdrop").hasClass("open")) { $("#settings-modal-backdrop").removeClass("open"); }
    });

    // ===== Search Logic =====
    function performSearch(term) {
        var $results = $("#search-results").empty();
        if (!term || term.length < 2) { return; }
        var q = term.toLowerCase();

        var collectionMap = {};
        $.each(searchCache.collections, function (i, col) {
            $.each(col.bookUuids || [], function (j, uuid) {
                if (!collectionMap[uuid]) { collectionMap[uuid] = []; }
                collectionMap[uuid].push(col.name || "");
            });
        });

        var matches = [];
        $.each(searchCache.books, function (i, book) {
            if (matches.length >= 6) { return false; }
            var haystack = (book.title || "").toLowerCase();
            haystack += " " + ((book.authors || []).join(" ")).toLowerCase();
            haystack += " " + (book.seriesName || "").toLowerCase();
            haystack += " " + ((collectionMap[book.uuid] || []).join(" ")).toLowerCase();
            if (haystack.indexOf(q) !== -1) { matches.push(book); }
        });

        if (matches.length === 0) { return; }

        $.each(matches, function (i, book) {
            var status = readingStatusText(book.readingProgress);
            var authors = (book.authors || []).join(", ");
            var series = book.seriesName ? (book.seriesName.toUpperCase() + (book.seriesIndex ? " - " + book.seriesIndex : "")) : "";
            $results.append(
                '<div class="book-list-item" data-search-uuid="' + book.uuid + '">' +
                    bookCoverHtml(book, "book-list-cover-img") +
                    '<div class="book-list-info">' +
                        '<div class="book-list-title">' + escapeHtml(book.title) + '</div>' +
                        '<div class="book-list-series">' + escapeHtml(series) + '</div>' +
                        '<div class="book-list-author">' + escapeHtml(authors) + '</div>' +
                    '</div>' +
                    '<div class="book-list-right">' +
                        '<div class="book-list-read-status">' + escapeHtml(status) + '</div>' +
                    '</div>' +
                '</div>'
            );
        });
    }

    $("#search-input").on("input", function () {
        var term = $(this).val();
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () {
            if (searchCache) {
                performSearch(term);
            } else {
                $.when($.get("/api/alexandria/books"), $.get("/api/alexandria/collections")).done(function (booksRes, colsRes) {
                    searchCache = { books: booksRes[0], collections: colsRes[0] };
                    performSearch(term);
                });
            }
        }, 300);
    });

    $(document).on("click", "[data-search-uuid]", function () {
        var uuid = $(this).data("search-uuid");
        closeSearchModal();
        openBookModal(uuid);
    });

    // ===== Modal Close =====
    $("#bm-close").on("click", closeBookModal);
    $("#book-modal-backdrop").on("click", function (e) {
        if (e.target === this) { closeBookModal(); }
    });

    // ===== Modal Cover Lightbox =====
    $("#book-modal-backdrop").on("click", "#bm-cover-frame", function (e) {
        if (!currentModalUuid) { return; }
        var lightbox = GLightbox({ elements: [{ href: coverUrl(currentModalUuid), type: "image" }], closeOnOutsideClick: true });
        lightbox.open();
    });

    // ===== Modal Tabs =====
    $("#bm-tab-synopsis").on("click", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-details, #bm-tab-data").removeClass("active");
        $("#bm-synopsis").show();
        $("#bm-details, #bm-data").hide();
    });

    $("#bm-tab-details").on("click", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-synopsis, #bm-tab-data").removeClass("active");
        $("#bm-details").show();
        $("#bm-synopsis, #bm-data").hide();
    });

    $("#bm-tab-data").on("click", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-synopsis, #bm-tab-details").removeClass("active");
        $("#bm-data").show();
        $("#bm-synopsis, #bm-details").hide();
    });

    // ===== Book Card / List Item Click → Open Modal =====
    $(document).on("click", ".book-card[data-book-uuid]", function (e) {
        if ($(e.target).closest(".book-menu-dropdown").length) { return; }
        openBookModal($(this).data("book-uuid"));
    });

    $(document).on("click", ".book-list-item[data-book-uuid]", function (e) {
        if ($(e.target).closest(".book-menu-dropdown").length) { return; }
        openBookModal($(this).data("book-uuid"));
    });

    // ===== View Details from menu =====
    $(document).on("click", "[data-view-details]", function (e) {
        e.preventDefault();
        openBookModal($(this).data("view-details"));
    });

    // ===== Mark as finished / unread =====
    $(document).on("click", "[data-mark-finished]", function (e) {
        e.preventDefault();
        updateReadingStatus($(this).data("mark-finished"), "Finished");
    });

    $(document).on("click", "[data-mark-unread]", function (e) {
        e.preventDefault();
        updateReadingStatus($(this).data("mark-unread"), "ReadyToRead");
    });

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
        $dropdown.find(".sort-arrow").text("");
        $item.find(".check").text("\u2713");
        $item.find(".sort-arrow").text(sortArrow(newOrder));
        var label = $toggle.data("label") || "";
        $toggle.text(label + $item.data("value"));

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
    var savedView = localStorage.getItem("view") || "grid";
    var $gridInit = $(".view-grid-container");
    var $listInit = $(".view-list-container");
    if ($gridInit.length && $listInit.length && savedView === "list") {
        $gridInit.addClass("d-none");
        $listInit.removeClass("d-none");
    }

    $(document).on("click", "[data-view-toggle]", function (e) {
        e.preventDefault();
        var view = $(this).data("view-toggle");
        var $grid = $(".view-grid-container");
        var $list = $(".view-list-container");

        if ($grid.length && $list.length) {
            localStorage.setItem("view", view);
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

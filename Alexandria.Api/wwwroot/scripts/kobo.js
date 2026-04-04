// ===== API Helpers =====
var COVER_LARGE = { w: 400, h: 600 };
var COVER_SMALL = { w: 120, h: 180 };

function coverUrl(uuid, width, height) {
    var url = "/api/alexandria/books/" + uuid + "/cover";
    if (width && height) { url += "?width=" + width + "&height=" + height; }
    return url;
}

var LANGUAGES = {
    "af": "Afrikaans", "ar": "Arabic", "bg": "Bulgarian", "bn": "Bengali", "ca": "Catalan",
    "cs": "Czech", "cy": "Welsh", "da": "Danish", "de": "German", "el": "Greek",
    "en": "English", "es": "Spanish", "et": "Estonian", "fa": "Persian", "fi": "Finnish",
    "fr": "French", "ga": "Irish", "gl": "Galician", "he": "Hebrew", "hi": "Hindi",
    "hr": "Croatian", "hu": "Hungarian", "id": "Indonesian", "is": "Icelandic", "it": "Italian",
    "ja": "Japanese", "ka": "Georgian", "ko": "Korean", "lt": "Lithuanian", "lv": "Latvian",
    "mk": "Macedonian", "ms": "Malay", "nl": "Dutch", "no": "Norwegian", "pl": "Polish",
    "pt": "Portuguese", "ro": "Romanian", "ru": "Russian", "sk": "Slovak", "sl": "Slovenian",
    "sq": "Albanian", "sr": "Serbian", "sv": "Swedish", "th": "Thai", "tr": "Turkish",
    "uk": "Ukrainian", "vi": "Vietnamese", "zh": "Chinese"
};

function languageName(code) {
    if (!code) { return ""; }
    return LANGUAGES[code.toLowerCase()] || code;
}

function languageOptions(selected) {
    var html = "";
    for (var code in LANGUAGES) {
        html += '<option value="' + code + '"' + (selected && selected.toLowerCase() === code ? ' selected' : '') + '>' + LANGUAGES[code] + '</option>';
    }
    return html;
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

function statusMenuItems(uuid, rawStatus, isArchived) {
    var items = "";
    if (rawStatus !== "Finished") {
        items += '<li><a class="dropdown-item" href="#" data-mark-finished="' + uuid + '">Mark as finished</a></li>';
    }
    if (rawStatus === "Reading" || rawStatus === "Finished") {
        items += '<li><a class="dropdown-item" href="#" data-mark-unread="' + uuid + '">Mark as unread</a></li>';
    }
    items += '<li><a class="dropdown-item" href="#" data-edit-book="' + uuid + '">Edit Book</a></li>';
    if (isArchived) {
        items += '<li><a class="dropdown-item" href="#" data-unarchive="' + uuid + '">Unarchive</a></li>';
    } else {
        items += '<li><a class="dropdown-item" href="#" data-archive="' + uuid + '">Archive</a></li>';
    }
    return items;
}

function updateReadingStatus(uuid, status) {
    $.ajax({ url: "/api/alexandria/books/" + uuid + "/status", method: "PUT", contentType: "application/json", data: JSON.stringify({ status: status }), success: function () { if (pageReload) { pageReload(); } if (currentModalUuid === uuid) { openBookModal(uuid); } } });
}

function updateArchiveStatus(uuid, isArchived) {
    $.ajax({ url: "/api/alexandria/books/" + uuid + "/archive", method: "PUT", contentType: "application/json", data: JSON.stringify({ isArchived: isArchived }), success: function () { if (pageReload) { pageReload(); } if (currentModalUuid === uuid) { openBookModal(uuid); } } });
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
    var hasCover = !!book.coverFilePath;
    return '<div class="cover-frame ' + cls + '">' +
           (hasCover ? '<img loading="lazy" src="' + coverUrl(book.uuid, size.w, size.h) + '" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' : '') +
           '<div class="book-cover" style="' + (hasCover ? 'display:none;' : '') + 'background:' + fallbackColor(book.uuid) + ';">' + escapeHtml(book.title) + '<small>' + escapeHtml((book.authors || []).join(", ")) + '</small></div></div>';
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
        var extraItems = statusMenuItems(book.uuid, rawStatus, book.isArchived);
        var archivedClass = book.isArchived ? ' book-card-archived' : '';
        var archivedLabel = book.isArchived ? '<span class="book-archived-label">Archived</span>' : '';
        var archivedListClass = book.isArchived ? ' book-list-item-archived' : '';
        var archivedListLabel = book.isArchived ? '<div class="book-list-archived-label">Archived</div>' : '';

        $grid.append(
            '<div class="book-card' + archivedClass + '" data-book-uuid="' + book.uuid + '">' +
                bookCoverHtml(book, "book-cover-img") +
                '<div class="book-status">' + escapeHtml(status) + archivedLabel + '</div>' +
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
            '<div class="book-list-item' + archivedListClass + '" data-book-uuid="' + book.uuid + '">' +
                bookCoverHtml(book, "book-list-cover-img") +
                '<div class="book-list-info">' +
                    '<div class="book-list-title">' + escapeHtml(book.title) + '</div>' +
                    '<div class="book-list-series">' + escapeHtml(series) + '</div>' +
                    '<div class="book-list-author">' + escapeHtml(authors) + '</div>' +
                '</div>' +
                '<div class="book-list-right">' +
                    '<div class="book-list-read-status">' + escapeHtml(status) + '</div>' +
                    archivedListLabel +
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

        $("#bm-cover-fallback").css("background", fallbackColor(book.uuid)).html(escapeHtml(book.title) + '<small>' + escapeHtml(authors) + '</small>');
        if (book.coverFilePath) {
            $("#bm-cover").attr("src", coverUrl(book.uuid, COVER_LARGE.w, COVER_LARGE.h)).show();
            $("#bm-cover-fallback").hide();
        } else {
            $("#bm-cover").hide();
            $("#bm-cover-fallback").show();
        }
        $("#bm-cover-frame").show();
        $("#bm-title").text(book.title);
        $("#bm-series").text(series).toggle(!!series);
        $("#bm-author").text(authors);
        var modalStatusItems = statusMenuItems(book.uuid, rawStatus, book.isArchived);
        $("#bm-status").text(status + (book.isArchived ? " \u00B7 Archived" : ""));
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
        if (book.language) { details += "<div class='detail-row'>Language: " + escapeHtml(languageName(book.language)) + "</div>"; }
        if (book.kepubFileSize) { details += "<div class='detail-row'>File Size: " + formatFileSize(book.kepubFileSize) + "</div>"; }
        details += "<div class='detail-row'>File Format: KOBO EPUB</div>";
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
    if (originalModalHtml) {
        $("#book-modal").html(originalModalHtml);
        originalModalHtml = null;
    }
    $("#book-modal-backdrop").removeClass("open");
}

var originalModalHtml = null;
function openEditModal(uuid) {
    $.get("/api/alexandria/books/" + uuid, function (book) {
        currentModalUuid = book.uuid;
        originalModalHtml = $("#book-modal").html();

        var authors = (book.authors || []).join(", ");
        var coverSrc = coverUrl(book.uuid, COVER_LARGE.w, COVER_LARGE.h);

        $("#book-modal").html(
            '<div class="book-modal-title-bar">Edit Book</div>' +
            '<button class="book-modal-close" id="bm-edit-close">&times;</button>' +
            '<div class="bm-edit-form">' +
                '<div class="bm-edit-cover-col">' +
                    '<div class="cover-frame book-modal-cover" style="cursor:pointer;position:relative;"><img id="bm-edit-cover-img" src="' + coverSrc + '" alt="" onerror="this.style.display=\'none\';this.parentElement.querySelector(\'.book-cover\').style.display=\'flex\';">' +
                    '<div class="book-cover" style="display:none;background:' + fallbackColor(book.uuid) + ';">' + escapeHtml(book.title) + '</div>' +
                    '<button class="bm-edit-cover-remove" id="bm-edit-cover-remove" title="Remove cover">&times;</button></div>' +
                    '<label class="bm-edit-btn bm-edit-cover-upload">' +
                        '<input type="file" id="bm-edit-cover-file" accept="image/*" style="display:none;">' +
                        '<span>Upload Cover</span>' +
                    '</label>' +
                '</div>' +
                '<div class="bm-edit-fields">' +
                    '<div class="bm-edit-field">' +
                        '<div class="bm-edit-label">Title</div>' +
                        '<input type="text" class="bm-edit-input" id="bm-edit-title" value="' + escapeHtml(book.title) + '">' +
                    '</div>' +
                    '<div class="bm-edit-field">' +
                        '<div class="bm-edit-label">Author(s) <span class="bm-edit-hint">Separate multiple authors with commas</span></div>' +
                        '<input type="text" class="bm-edit-input" id="bm-edit-authors" value="' + escapeHtml(authors) + '">' +
                    '</div>' +
                    '<div class="bm-edit-row">' +
                        '<div class="bm-edit-field" style="flex:1;">' +
                            '<div class="bm-edit-label">Language</div>' +
                            '<select class="bm-edit-input" id="bm-edit-language">' + languageOptions(book.language) + '</select>' +
                        '</div>' +
                        '<div class="bm-edit-field" style="flex:1;">' +
                            '<div class="bm-edit-label">Publisher</div>' +
                            '<input type="text" class="bm-edit-input" id="bm-edit-publisher" value="' + escapeHtml(book.publisher || "") + '">' +
                        '</div>' +
                    '</div>' +
                    '<div class="bm-edit-row">' +
                        '<div class="bm-edit-field" style="flex:1;">' +
                            '<div class="bm-edit-label">Series Name</div>' +
                            '<input type="text" class="bm-edit-input" id="bm-edit-series-name" value="' + escapeHtml(book.seriesName || "") + '">' +
                        '</div>' +
                        '<div class="bm-edit-field" style="width:100px;">' +
                            '<div class="bm-edit-label">Series Index</div>' +
                            '<input type="number" class="bm-edit-input" id="bm-edit-series-index" value="' + (book.seriesIndex != null ? book.seriesIndex : "") + '">' +
                        '</div>' +
                    '</div>' +
                    '<div class="bm-edit-field">' +
                        '<div class="bm-edit-label">Publication Date</div>' +
                        '<input type="text" class="bm-edit-input" id="bm-edit-pub-date" value="' + escapeHtml(book.publicationDate || "") + '">' +
                    '</div>' +
                '</div>' +
            '</div>' +
            '<div class="bm-edit-field" style="margin-top:16px;">' +
                '<div class="bm-edit-label">Description</div>' +
                '<textarea class="bm-edit-textarea" id="bm-edit-description">' + escapeHtml(book.description || "") + '</textarea>' +
            '</div>' +
            '<div class="bm-edit-footer">' +
                '<div class="bm-edit-warning">Changing title or authors will change the book\'s unique ID. Sync reading status before editing if the book is already on your device. Removing a series from a book will not remove it from the Kobo device — this requires a factory reset or a reindex with a new ID seed.</div>' +
                '<div class="bm-edit-buttons"><button class="bm-edit-btn" id="bm-edit-cancel">Cancel</button>' +
                '<button class="bm-edit-btn bm-edit-btn-primary" id="bm-edit-save">Save</button></div>' +
            '</div>'
        );

        $("#book-modal-backdrop").addClass("open");
    });
}

function showEditLoading(target) {
    var $el = $(target);
    $el.data("original-text", $el.text()).text("Loading...").prop("disabled", true);
    $(".bm-edit-btn").prop("disabled", true).addClass("bm-edit-btn-disabled");
    $(".bm-edit-input, .bm-edit-textarea").prop("disabled", true).addClass("bm-edit-input-disabled");
}

function hideEditLoading(target) {
    var $el = $(target);
    $el.text($el.data("original-text") || "").prop("disabled", false);
    $(".bm-edit-btn").prop("disabled", false).removeClass("bm-edit-btn-disabled");
    $(".bm-edit-input, .bm-edit-textarea").prop("disabled", false).removeClass("bm-edit-input-disabled");
}

function saveBookMetadata(uuid) {
    var seriesIdx = $("#bm-edit-series-index").val();
    var book = {
        title: $("#bm-edit-title").val(),
        authors: $("#bm-edit-authors").val().split(",").map(function (a) { return a.trim(); }).filter(function (a) { return a; }),
        language: $("#bm-edit-language").val(),
        publisher: $("#bm-edit-publisher").val() || null,
        seriesName: $("#bm-edit-series-name").val() || null,
        seriesIndex: seriesIdx !== "" ? parseInt(seriesIdx, 10) : null,
        publicationDate: $("#bm-edit-pub-date").val() || null,
        description: $("#bm-edit-description").val() || null
    };

    showEditLoading("#bm-edit-save");
    $.ajax({
        url: "/api/alexandria/books/" + uuid + "/metadata",
        method: "PUT",
        contentType: "application/json",
        data: JSON.stringify(book),
        success: function () {
            originalModalHtml = null;
            if (pageReload) { pageReload(); }
            openBookModal(uuid);
        },
        complete: function () { hideEditLoading("#bm-edit-save"); }
    });
}

function uploadBookCover(uuid, file) {
    var formData = new FormData();
    formData.append("cover", file);
    showEditLoading(".bm-edit-cover-upload span");
    $.ajax({
        url: "/api/alexandria/books/" + uuid + "/cover",
        method: "PUT",
        data: formData,
        processData: false,
        contentType: false,
        success: function () {
            $("#bm-edit-cover-img").attr("src", coverUrl(uuid, COVER_LARGE.w, COVER_LARGE.h) + "&t=" + Date.now()).show().next().hide();
            if (pageReload) { pageReload(); }
        },
        complete: function () { hideEditLoading(".bm-edit-cover-upload span"); }
    });
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

    // ===== Inject Sync Popover HTML =====
    $("body").append(
        '<div class="sync-popover-backdrop" id="sync-popover-backdrop">' +
            '<div class="sync-popover" id="sync-popover">' +
                '<div class="sync-popover-text" id="sync-popover-text">Sync library files for changes.</div>' +
                '<div class="sync-popover-progress" id="sync-popover-progress"><div class="sync-popover-progress-bar" id="sync-popover-progress-bar"></div></div>' +
                '<button class="sync-popover-btn" id="sync-popover-btn">Sync now</button>' +
                '<div class="sync-popover-last-sync" id="sync-popover-last-sync">LAST SYNC: NEVER</div>' +
            '</div>' +
        '</div>'
    );

    // ===== Sync Popover Open/Close =====
    var syncInProgress = false;
    var syncCompleted = false;

    function formatLastIndexed(isoString) {
        if (!isoString) { return "NEVER"; }
        var d = new Date(isoString);
        var now = new Date();
        var hh = String(d.getHours()).padStart(2, "0");
        var mm = String(d.getMinutes()).padStart(2, "0");
        if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate()) {
            return hh + ":" + mm;
        }
        var yyyy = d.getFullYear();
        var mo = String(d.getMonth() + 1).padStart(2, "0");
        var dd = String(d.getDate()).padStart(2, "0");
        return yyyy + "-" + mo + "-" + dd + " " + hh + ":" + mm;
    }

    function openSyncPopover() {
        if (!syncInProgress) {
            $("#sync-popover-text").text("Sync library files for changes.");
            $("#sync-popover-progress-bar").css("width", "0%");
            $("#sync-popover-btn").text("Sync now").prop("disabled", false);
            syncCompleted = false;
            $.get("/api/alexandria/settings", function (data) {
                $("#sync-popover-last-sync").text("LAST SYNC: " + formatLastIndexed(data.lastIndexedTimestamp));
            });
        }
        $("#sync-popover-backdrop").addClass("open");
    }

    function closeSyncPopover() {
        if (syncInProgress) { return; }
        $("#sync-popover-backdrop").removeClass("open");
    }

    $("#sync-btn").on("click", function () {
        if ($("#sync-popover-backdrop").hasClass("open")) {
            closeSyncPopover();
        } else {
            openSyncPopover();
        }
    });

    $("#sync-popover-backdrop").on("click", function (e) {
        if (e.target === this) { closeSyncPopover(); }
    });

    // ===== Sync Now =====
    $("#sync-popover-btn").on("click", function () {
        if (syncInProgress) { return; }

        if (syncCompleted) {
            syncCompleted = false;
            $("#sync-popover-backdrop").removeClass("open");
            if (typeof pageReload === "function" && pageReload) {
                pageReload();
            } else {
                location.reload();
            }
            return;
        }

        syncInProgress = true;
        syncCompleted = false;
        $("#sync-popover-btn").text("Syncing...").prop("disabled", true);
        $("#sync-popover-progress-bar").css("width", "0%");

        fetch("/api/alexandria/sync", { method: "POST" }).then(function (response) {
            var reader = response.body.getReader();
            var decoder = new TextDecoder();

            function read() {
                reader.read().then(function (result) {
                    if (result.done) { return; }
                    var lines = decoder.decode(result.value).split("\n");
                    lines.forEach(function (line) {
                        if (!line.startsWith("data: ")) { return; }
                        var json = JSON.parse(line.substring(6));
                        if (json.done) {
                            $("#sync-popover-last-sync").text("LAST SYNC: " + formatLastIndexed(json.lastIndexedTimestamp));
                            $("#sync-popover-progress-bar").css("width", "100%");
                            $("#sync-popover-text").text("Sync complete. " + json.bookCount + " books indexed.");
                            $("#sync-popover-btn").text("Close").prop("disabled", false);
                            syncInProgress = false;
                            syncCompleted = true;
                        } else {
                            var pct = Math.round((json.current / json.total) * 100);
                            $("#sync-popover-progress-bar").css("width", pct + "%");
                        }
                    });
                    read();
                });
            }
            read();
        }).catch(function () {
            syncInProgress = false;
            syncCompleted = false;
            $("#sync-popover-text").text("Sync failed. Please try again.");
            $("#sync-popover-btn").text("Sync now").prop("disabled", false);
        });
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
        if (e.key === "Escape" && $("#sync-popover-backdrop").hasClass("open")) { closeSyncPopover(); }
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
            var archivedSearchClass = book.isArchived ? ' book-list-item-archived' : '';
            var archivedSearchLabel = book.isArchived ? '<div class="book-list-archived-label">Archived</div>' : '';
            $results.append(
                '<div class="book-list-item' + archivedSearchClass + '" data-search-uuid="' + book.uuid + '">' +
                    bookCoverHtml(book, "book-list-cover-img") +
                    '<div class="book-list-info">' +
                        '<div class="book-list-title">' + escapeHtml(book.title) + '</div>' +
                        '<div class="book-list-series">' + escapeHtml(series) + '</div>' +
                        '<div class="book-list-author">' + escapeHtml(authors) + '</div>' +
                    '</div>' +
                    '<div class="book-list-right">' +
                        '<div class="book-list-read-status">' + escapeHtml(status) + '</div>' +
                        archivedSearchLabel +
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
    $(document).on("click", "#bm-close", function () { closeBookModal(); });
    $("#book-modal-backdrop").on("click", function (e) {
        if (e.target === this) { closeBookModal(); }
    });

    // ===== Modal Cover Lightbox =====
    $(document).on("click", "#bm-cover-frame, #bm-edit-cover-img", function (e) {
        if (!currentModalUuid) { return; }
        var src = coverUrl(currentModalUuid);
        var lightbox = GLightbox({ elements: [{ href: src, type: "image" }], closeOnOutsideClick: true });
        lightbox.open();
    });

    // ===== Modal Tabs =====
    $(document).on("click", "#bm-tab-synopsis", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-details, #bm-tab-data").removeClass("active");
        $("#bm-synopsis").show();
        $("#bm-details, #bm-data").hide();
    });

    $(document).on("click", "#bm-tab-details", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-synopsis, #bm-tab-data").removeClass("active");
        $("#bm-details").show();
        $("#bm-synopsis, #bm-data").hide();
    });

    $(document).on("click", "#bm-tab-data", function (e) {
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

    // ===== Archive / Unarchive =====
    $(document).on("click", "[data-archive]", function (e) {
        e.preventDefault();
        updateArchiveStatus($(this).data("archive"), true);
    });

    $(document).on("click", "[data-unarchive]", function (e) {
        e.preventDefault();
        updateArchiveStatus($(this).data("unarchive"), false);
    });

    // ===== Edit Book =====
    $(document).on("click", "[data-edit-book]", function (e) {
        e.preventDefault();
        openEditModal($(this).data("edit-book"));
    });

    $(document).on("click", "#bm-edit-save", function () {
        if (currentModalUuid) { saveBookMetadata(currentModalUuid); }
    });

    $(document).on("click", "#bm-edit-close", function () {
        closeBookModal();
    });

    $(document).on("click", "#bm-edit-cancel", function () {
        if (currentModalUuid && originalModalHtml) {
            $("#book-modal").html(originalModalHtml);
            originalModalHtml = null;
            openBookModal(currentModalUuid);
        } else {
            closeBookModal();
        }
    });

    $(document).on("change", "#bm-edit-cover-file", function () {
        var file = this.files[0];
        if (file && currentModalUuid) {
            $("#bm-edit-cover-img").attr("src", URL.createObjectURL(file)).show().next().hide();
            uploadBookCover(currentModalUuid, file);
        }
    });

    $(document).on("click", "#bm-edit-cover-remove", function (e) {
        e.stopPropagation();
        if (!currentModalUuid) { return; }
        var uuid = currentModalUuid;
        $.ajax({
            url: "/api/alexandria/books/" + uuid + "/cover",
            type: "DELETE",
            success: function () {
                $("#bm-edit-cover-img").hide();
                $(".bm-edit-cover-col .book-cover").show();
                if (pageReload) { pageReload(); }
            },
            error: function () {
                $("#bm-edit-cover-img").hide();
                $(".bm-edit-cover-col .book-cover").show();
                if (pageReload) { pageReload(); }
            }
        });
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

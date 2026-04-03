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
    return '<div class="cover-frame ' + cls + '"><img src="' + coverUrl(book.uuid) + '" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' +
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

// ===== Shared renderBooks =====
function renderBooks(books) {
    var $grid = $("#book-grid").empty();
    var $list = $("#book-list").empty();
    $.each(books, function (i, book) {
        var status = readingStatusText(book.readingProgress);
        var authors = (book.authors || []).join(", ");
        var series = book.seriesName ? (book.seriesName.toUpperCase() + (book.seriesIndex ? " - " + book.seriesIndex : "")) : "";

        $grid.append(
            '<div class="book-card" data-book-uuid="' + book.uuid + '">' +
                bookCoverHtml(book, "book-cover-img") +
                '<div class="book-status">' + escapeHtml(status) + '</div>' +
                '<div class="dropdown book-menu-dropdown">' +
                    '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                    '<ul class="dropdown-menu dropdown-menu-end">' +
                        '<li><a class="dropdown-item" href="#" data-view-details="' + book.uuid + '">View Details</a></li>' +
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
                    '<div class="book-list-format">KEPUB - ' + formatFileSize(book.kepubFileSize) + '</div>' +
                    '<div class="dropdown book-menu-dropdown">' +
                        '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                        '<ul class="dropdown-menu dropdown-menu-end">' +
                            '<li><a class="dropdown-item" href="#" data-view-details="' + book.uuid + '">View Details</a></li>' +
                        '</ul>' +
                    '</div>' +
                '</div>' +
            '</div>'
        );
    });
}

// ===== Book Detail Modal =====
function openBookModal(uuid) {
    $.get("/api/alexandria/books/" + uuid, function (book) {
        var authors = (book.authors || []).join(", ");
        var series = book.seriesName ? (book.seriesName.toUpperCase() + (book.seriesIndex ? " - " + book.seriesIndex : "")) : "";
        var status = readingStatusText(book.readingProgress);

        $("#bm-cover").attr("src", coverUrl(book.uuid)).show();
        $("#bm-cover-frame").show();
        $("#bm-cover-fallback").hide().css("background", fallbackColor(book.uuid)).html(escapeHtml(book.title) + '<small>' + escapeHtml(authors) + '</small>');
        $("#bm-title").text(book.title);
        $("#bm-series").text(series).toggle(!!series);
        $("#bm-author").text(authors);
        $("#bm-status").text(status);

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
        $("#bm-data-code").text(JSON.stringify(book, null, 2));

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
    var $active = $items.filter("[data-sort='" + sort + "']");
    if ($active.length) {
        $active.find(".check").text("\u2713");
        var label = $toggle.data("label") || "";
        $toggle.text(label + $active.data("value") + sortArrow(order));
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
                        '<div class="book-modal-status" id="bm-status"></div>' +
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

    // ===== Modal Close =====
    $("#bm-close").on("click", closeBookModal);
    $("#book-modal-backdrop").on("click", function (e) {
        if (e.target === this) { closeBookModal(); }
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
    $(document).on("click", "[data-view-toggle]", function (e) {
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

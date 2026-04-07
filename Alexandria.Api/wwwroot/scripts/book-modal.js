// ===== Book Detail Modal =====
var currentModalUuid = null;
var originalModalHtml = null;

function updateReadingStatus(uuid, status) {
    $.ajax({ url: "/api/alexandria/books/" + uuid + "/status", method: "PUT", contentType: "application/json", data: JSON.stringify({ status: status }), success: function () { if (pageReload) { pageReload(); } if (currentModalUuid === uuid) { openBookModal(uuid); } } });
}

function updateArchiveStatus(uuid, isArchived) {
    $.ajax({ url: "/api/alexandria/books/" + uuid + "/archive", method: "PUT", contentType: "application/json", data: JSON.stringify({ isArchived: isArchived }), success: function () { if (pageReload) { pageReload(); } if (currentModalUuid === uuid) { openBookModal(uuid); } } });
}

function openBookModal(uuid) {
    $.get("/api/alexandria/books/" + uuid, function (book) {
        currentModalUuid = book.bookUuid;
        var authors = (book.authors || []).map(function(a){return a.name}).join(", ");
        var seriesName = book.series ? book.series.name : "";
        var series = seriesName ? (seriesName.toUpperCase() + ((book.series ? book.series.index : null) ? " - " + (book.series ? book.series.index : null) : "")) : "";
        var status = readingStatusText(book.readingProgress);
        var rawStatus = readingStatusRaw(book.readingProgress);

        $("#bm-cover-fallback").css("background", fallbackColor(book.bookUuid)).html(escapeHtml(book.title) + '<small>' + escapeHtml(authors) + '</small>');
        if (book.hasCover) {
            $("#bm-cover").attr("src", coverUrl(book.bookUuid, COVER_LARGE.w, COVER_LARGE.h)).show();
            $("#bm-cover-fallback").hide();
        } else {
            $("#bm-cover").hide();
            $("#bm-cover-fallback").show();
        }
        $("#bm-cover-frame").show();
        $("#bm-title").text(book.title);
        var seriesHtml = seriesName ? ('<a href="series-detail.html?uuid=' + book.series.seriesUuid + '">' + escapeHtml(series) + '</a>') : "";
        $("#bm-series").html(seriesHtml).toggle(!!seriesName);
        var authorLinks = (book.authors || []).map(function(a) {
            return '<a href="author-detail.html?uuid=' + a.authorUuid + '">' + escapeHtml(a.name) + '</a>';
        }).join(", ");
        $("#bm-author").html(authorLinks);
        var modalStatusItems = statusMenuItems(book.bookUuid, rawStatus, book.isArchived);
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
    currentModalUuid = null;
    if (originalModalHtml) {
        $("#book-modal").html(originalModalHtml);
        originalModalHtml = null;
    }
    $("#book-modal-backdrop").removeClass("open");
}

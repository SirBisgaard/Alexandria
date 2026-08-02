// ===== Book Detail Modal =====
var currentModalUuid = null;
var currentModalModifiedAt = null;
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
        currentModalModifiedAt = book.modifiedAt;
        var authors = (book.authors || []).map(function(a){return a.name}).join(", ");
        var seriesName = book.series ? book.series.name : "";
        var series = seriesName ? (seriesName.toUpperCase() + ((book.series ? book.series.index : null) ? " - " + (book.series ? book.series.index : null) : "")) : "";
        var status = readingStatusText(book.readingProgress, book.file.mediaType);
        var rawStatus = readingStatusRaw(book.readingProgress);

        $("#bm-cover-fallback").css("background", fallbackColor(book.bookUuid)).html(escapeHtml(book.title) + '<small>' + escapeHtml(authors) + '</small>');
        if (book.file.hasCover) {
            $("#bm-cover").attr("src", coverUrl(book.bookUuid, COVER_LARGE.w, COVER_LARGE.h, book.modifiedAt)).show();
            $("#bm-cover-fallback").hide();
        } else {
            $("#bm-cover").hide();
            $("#bm-cover-fallback").show();
        }
        $("#bm-cover-inner .audiobook-badge").remove();
        if (book.file.mediaType === "Audiobook")
            $("#bm-cover-inner").append(AUDIOBOOK_BADGE);
        $("#bm-cover-frame").show();
        $("#bm-title").text(book.title);
        var seriesHtml = seriesName ? ('<a href="#/series/' + book.series.seriesUuid + '">' + escapeHtml(series) + '</a>') : "";
        $("#bm-series").html(seriesHtml).toggle(!!seriesName);
        var authorLinks = (book.authors || []).map(function(a) {
            return '<a href="#/authors/' + a.authorUuid + '">' + escapeHtml(a.name) + '</a>';
        }).join(", ");
        $("#bm-author").html(authorLinks);
        var contributorHtml = (book.contributors || []).map(function(c) {
            return escapeHtml(c.name) + (c.role ? ' <span class="book-modal-contributor-role">(' + escapeHtml(c.role) + ')</span>' : '');
        }).join(", ");
        if (contributorHtml)
            $("#bm-contributors").html(contributorHtml).show();
        else
            $("#bm-contributors").hide();
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
        if (book.file.mediaType === "Audiobook" && book.file.duration) {
            var hours = (book.file.duration / 3600).toFixed(1);
            details += "<div class='detail-row'>" + hours + " hours</div>";
            details += "<div class='detail-row mb-3'>" + (book.file.isAbridged ? "Abridged" : "Unabridged") + "</div>";
        }
        if (book.file.pageCount) {
            details += "<div class='detail-row'>" + book.file.pageCount + " pages</div>";
        }
        if (book.file.wordCount) {
            var words = book.file.wordCount >= 1000 ? Math.round(book.file.wordCount / 1000) + "k" : book.file.wordCount;
            details += "<div class='detail-row mb-3'>" + words + " total words</div>";
        }
        if (book.publisher) { details += "<div class='detail-row'>Publisher: " + escapeHtml(book.publisher) + (book.publicationDate ? " (" + escapeHtml(book.publicationDate) + ")" : "") + "</div>"; }
        if (book.isbn) { details += "<div class='detail-row'>ISBN: " + escapeHtml(book.isbn) + "</div>"; }
        if (book.language) { details += "<div class='detail-row'>Language: " + escapeHtml(languageName(book.language)) + "</div>"; }
        if (book.file.fileName) { details += "<div class='detail-row'>File Name: " + escapeHtml(book.file.fileName) + "</div>"; }
        if (book.file.totalFileSize) { details += "<div class='detail-row'>File Size: " + formatFileSize(book.file.totalFileSize) + "</div>"; }
        details += "<div class='detail-row'>File Format: " + (book.file.mediaType === "Audiobook" ? "AUDIOBOOK" : "KOBO EPUB") + "</div>";
        $("#bm-details").html(details || "No details available.");

        // Data
        $("#bm-data-code").html(highlightJson(book));

        // Events tab lazy-loads on click — reset to placeholder so a new modal doesn't show
        // the prior book's events while the next fetch is in flight.
        $("#bm-events-list").html('<p class="bm-events-empty">Open this tab to load events.</p>');

        // Show synopsis tab by default
        $("#bm-tab-synopsis").addClass("active");
        $("#bm-tab-details").removeClass("active");
        $("#bm-tab-events").removeClass("active");
        $("#bm-tab-data").removeClass("active");
        $("#bm-synopsis").show();
        $("#bm-details").hide();
        $("#bm-events").hide();
        $("#bm-data").hide();

        $("#book-modal-backdrop").addClass("open");
    });
}

function loadBookEvents(uuid) {
    $("#bm-events-list").html('<p class="bm-events-empty">Loading…</p>');
    $.get("/api/alexandria/books/" + uuid + "/events", function (events) {
        if (uuid !== currentModalUuid) { return; }
        if (!events || events.length === 0) {
            $("#bm-events-list").html('<p class="bm-events-empty">No events recorded for this book.</p>');
            return;
        }
        var rows = events.map(function (e) {
            var when = e.eventTimestamp ? new Date(e.eventTimestamp).toLocaleString() : "";
            return '<tr>' +
                '<td class="bm-events-time">' + escapeHtml(when) + '</td>' +
                '<td class="bm-events-type">' + escapeHtml(e.eventType || "") + '</td>' +
                '<td class="bm-events-info">' + escapeHtml(e.description || "") + '</td>' +
                '</tr>';
        }).join("");
        $("#bm-events-list").html(
            '<table class="bm-events-table">' +
                '<thead><tr><th>Time</th><th>Type</th><th>Info</th></tr></thead>' +
                '<tbody>' + rows + '</tbody>' +
            '</table>'
        );
    });
}

function closeBookModal() {
    currentModalUuid = null;
    currentModalModifiedAt = null;
    if (originalModalHtml) {
        $("#book-modal").html(originalModalHtml);
        originalModalHtml = null;
    }
    $("#book-modal-backdrop").removeClass("open");
}

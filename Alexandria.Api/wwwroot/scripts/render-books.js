// ===== Shared renderBooks =====
function renderBooks(books) {
    var $grid = $("#book-grid").empty();
    var $list = $("#book-list").empty();
    $.each(books, function (i, book) {
        var status = readingStatusText(book.readingProgress, book.file.mediaType);
        var rawStatus = readingStatusRaw(book.readingProgress);
        var authors = (book.authors || []).map(function(a){return a.name}).join(", ");
        var contributors = (book.contributors || []).map(function(c){ return c.name + (c.role ? " (" + c.role + ")" : ""); }).join(", ");
        var seriesName = book.series ? book.series.name : "";
        var series = seriesName ? (seriesName.toUpperCase() + ((book.series ? book.series.index : null) ? " - " + (book.series ? book.series.index : null) : "")) : "";
        var extraItems = statusMenuItems(book.bookUuid, rawStatus, book.isArchived);
        var archivedClass = book.isArchived ? ' book-card-archived' : '';
        var archivedLabel = book.isArchived ? '<span class="book-archived-label">Archived</span>' : '';
        var archivedListClass = book.isArchived ? ' book-list-item-archived' : '';
        var archivedListLabel = book.isArchived ? '<div class="book-list-archived-label">Archived</div>' : '';

        $grid.append(
            '<div class="col"><div class="book-card' + archivedClass + '" data-book-uuid="' + book.bookUuid + '">' +
                bookCoverHtml(book, "book-cover-img") +
                '<div class="book-status">' + escapeHtml(status) + archivedLabel + '</div>' +
                '<div class="dropdown book-menu-dropdown">' +
                    '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                    '<ul class="dropdown-menu dropdown-menu-end">' +
                        '<li><a class="dropdown-item" href="#" data-view-details="' + book.bookUuid + '">View Details</a></li>' +
                        extraItems +
                    '</ul>' +
                '</div>' +
            '</div></div>'
        );

        $list.append(
            '<div class="book-list-item' + archivedListClass + '" data-book-uuid="' + book.bookUuid + '">' +
                bookCoverHtml(book, "book-list-cover-img") +
                '<div class="book-list-info">' +
                    '<div class="book-list-title">' + escapeHtml(book.title) + '</div>' +
                    '<div class="book-list-series">' + escapeHtml(series) + '</div>' +
                    '<div class="book-list-author">' + escapeHtml(authors) + '</div>' +
                    (contributors ? '<div class="book-list-contributor">' + escapeHtml(contributors) + '</div>' : '') +
                '</div>' +
                '<div class="book-list-right">' +
                    '<div class="book-list-read-status">' + escapeHtml(status) + '</div>' +
                    archivedListLabel +
                    (book.readingProgress && book.readingProgress.remainingTimeMinutes ? '<div class="book-list-time-remaining">' + Math.round(book.readingProgress.remainingTimeMinutes / 60) + ' hours to go</div>' : '') +
                    '<div class="book-list-format">' + (book.file.mediaType === "Audiobook" ? "AUDIOBOOK" : "KEPUB") + ' - ' + formatFileSize(book.file.totalFileSize) + '</div>' +
                    '<div class="dropdown book-menu-dropdown">' +
                        '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                        '<ul class="dropdown-menu dropdown-menu-end">' +
                            '<li><a class="dropdown-item" href="#" data-view-details="' + book.bookUuid + '">View Details</a></li>' +
                            extraItems +
                        '</ul>' +
                    '</div>' +
                '</div>' +
            '</div>'
        );
    });
}

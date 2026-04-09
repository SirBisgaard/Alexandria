// ===== Search Modal =====
$(function () {
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

    window.closeSearchModal = closeSearchModal;

    $("#search-btn").on("click", openSearchModal);
    $("#search-close").on("click", closeSearchModal);
    $("#search-modal-backdrop").on("click", function (e) {
        if (e.target === this) { closeSearchModal(); }
    });

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
            haystack += " " + ((book.authors || []).map(function(a){return a.name}).join(" ")).toLowerCase();
            haystack += " " + (book.series ? book.series.name : "").toLowerCase();
            haystack += " " + ((collectionMap[book.bookUuid] || []).join(" ")).toLowerCase();
            if (haystack.indexOf(q) !== -1) { matches.push(book); }
        });

        if (matches.length === 0) { return; }

        $.each(matches, function (i, book) {
            var status = readingStatusText(book.readingProgress, book.mediaType);
            var authors = (book.authors || []).map(function(a){return a.name}).join(", ");
            var seriesName = book.series ? book.series.name : "";
            var series = seriesName ? (seriesName.toUpperCase() + ((book.series ? book.series.index : null) ? " - " + (book.series ? book.series.index : null) : "")) : "";
            var archivedSearchClass = book.isArchived ? ' book-list-item-archived' : '';
            var archivedSearchLabel = book.isArchived ? '<div class="book-list-archived-label">Archived</div>' : '';
            $results.append(
                '<div class="book-list-item' + archivedSearchClass + '" data-search-uuid="' + book.bookUuid + '">' +
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
});

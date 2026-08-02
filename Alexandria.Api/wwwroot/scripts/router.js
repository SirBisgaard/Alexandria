// ===== Hash Router =====
// Routes map #/segment[/param] to a view template plus an init function. Exactly one view's
// markup lives in #view-root at a time, so per-view element ids never collide across templates.

function initHomeView() {
    $.when(
        $.get("/api/alexandria/books"),
        $.get("/api/alexandria/collections"),
        $.get("/api/alexandria/series?sort=Recent&order=Desc")
    ).done(function (booksResult, collectionsResult, seriesResult) {
        var books = booksResult[0].filter(function (b) { return !b.isArchived; });
        var collections = collectionsResult[0];
        var seriesList = seriesResult[0];
        var $grid = $("#home-grid");

        // Currently reading — up to 5 books sorted by last read
        // TODO: comment says 5 but slice below uses 6 — reconcile so the index shows the intended number of books
        var readingBooks = [];
        for (var i = 0; i < books.length; i++) {
            var p = books[i].readingProgress;
            if (p && p.status === "Reading") {
                readingBooks.push(books[i]);
            }
        }
        readingBooks.sort(function (a, b) {
            return new Date(b.readingProgress.modifiedAt) - new Date(a.readingProgress.modifiedAt);
        });
        readingBooks = readingBooks.slice(0, 6);
        if (readingBooks.length > 0) {
            var spanClass = readingBooks.length >= 3 ? ' home-reading-span' : '';
            var readingHtml = '<div class="home-reading' + spanClass + '">';
            $.each(readingBooks, function (i, book) {
                var status = readingStatusText(book.readingProgress, book.file.mediaType);
                var subText = readingRemainingText(book.readingProgress);
                readingHtml += '<div class="home-reading-item" onclick="openBookModal(\'' + book.bookUuid + '\')">' +
                    '<div class="home-card-covers">' + homeSingleCoverHtml(book) + '</div>' +
                    '<div class="home-card-title">' + escapeHtml(status) + '</div>' +
                    '<div class="home-card-sub">' + escapeHtml(subText) + '</div>' +
                '</div>';
            });
            readingHtml += '</div>';
            var colClass = readingBooks.length > 2 ? 'col-12' : 'col-12 col-md-6';
            $grid.append('<div class="' + colClass + '">' + readingHtml + '</div>');
        }

        // Daily recommendation — 1-2 unread books, deterministic per day
        var eligible = books.filter(function (b) {
            return !b.readingProgress || b.readingProgress.status === "ReadyToRead";
        });
        if (eligible.length > 0) {
            var today = new Date().toISOString().slice(0, 10);
            var hash = 0;
            for (var c = 0; c < today.length; c++) {
                hash = ((hash << 5) - hash) + today.charCodeAt(c);
                hash |= 0;
            }
            var idx1 = Math.abs(hash) % eligible.length;
            var picks = [eligible[idx1]];
            if (eligible.length > 1) {
                var idx2 = Math.abs(hash * 31) % eligible.length;
                if (idx2 === idx1) idx2 = (idx2 + 1) % eligible.length;
                picks.push(eligible[idx2]);
            }
            var recHtml = '<div class="home-reading">';
            $.each(picks, function (i, book) {
                recHtml += '<div class="home-reading-item" onclick="openBookModal(\'' + book.bookUuid + '\')">' +
                    '<div class="home-card-covers">' + homeSingleCoverHtml(book) + '</div>' +
                '</div>';
            });
            recHtml += '</div><div class="home-card-title">Need something to read?</div>';
            $grid.append('<div class="col-12 col-md-6">' + recHtml + '</div>');
        }

        // My Books
        if (books.length > 0) {
            var firstThree = books.slice(0, 3);
            var myBooksStack = '<div class="home-card-covers"><div class="home-card-cover-stack">';
            $.each(firstThree, function (i, b) {
                myBooksStack += homeStackCoverHtml(b);
            });
            myBooksStack += '</div></div>';
            $grid.append(
                '<div class="col-12 col-md-6"><a href="#/books" class="home-card">' + myBooksStack +
                    '<div class="home-card-title">My Books</div>' +
                    '<div class="home-card-sub">' + books.length + ' books</div>' +
                '</a></div>'
            );
        }

        // Author spotlight — author with most unread books
        var authorMap = {};
        $.each(books, function (i, b) {
            $.each(b.authors || [], function (j, a) {
                if (!authorMap[a.authorUuid]) { authorMap[a.authorUuid] = { name: a.name, books: [] }; }
                authorMap[a.authorUuid].books.push(b);
            });
        });
        var topAuthorUuid = null;
        var topAuthorName = null;
        var topCount = 0;
        $.each(authorMap, function (uuid, data) {
            var unread = 0;
            $.each(data.books, function (i, b) {
                if (!b.readingProgress || b.readingProgress.status === "ReadyToRead") { unread++; }
            });
            if (unread > topCount) { topCount = unread; topAuthorUuid = uuid; topAuthorName = data.name; }
        });
        if (topAuthorUuid) {
            var aBooks = authorMap[topAuthorUuid].books.slice(0, 3);
            var stackHtml = '<div class="home-card-covers"><div class="home-card-cover-stack">';
            $.each(aBooks, function (i, b) {
                stackHtml += homeStackCoverHtml(b);
            });
            stackHtml += '</div></div>';
            $grid.append(
                '<div class="col-12 col-md-6"><a href="#/authors/' + topAuthorUuid + '" class="home-card">' + stackHtml +
                    '<div class="home-card-title">' + escapeHtml(topAuthorName) + '</div>' +
                    '<div class="home-card-sub">' + topCount + ' unread books</div>' +
                '</a></div>'
            );
        }

        // Latest series — skip series where all books are archived
        var visibleSeries = seriesList.filter(function (s) {
            return (s.books || []).some(function (b) { return !b.isArchived; });
        });
        if (visibleSeries.length > 0) {
            var latestSeries = visibleSeries[0];
            var sBooks = (latestSeries.books || []).filter(function (b) { return !b.isArchived; }).slice(0, 3);
            var sStack = '<div class="home-card-covers"><div class="home-card-cover-stack">';
            $.each(sBooks, function (i, b) {
                sStack += homeStackCoverHtml(b);
            });
            sStack += '</div></div>';
            var sCount = (latestSeries.books || []).filter(function (b) { return !b.isArchived; }).length;
            $grid.append(
                '<div class="col-12 col-md-6"><a href="#/series/' + latestSeries.seriesUuid + '" class="home-card">' + sStack +
                    '<div class="home-card-title">' + escapeHtml(latestSeries.name) + '</div>' +
                    '<div class="home-card-sub">' + sCount + (sCount === 1 ? ' book' : ' books') + '</div>' +
                '</a></div>'
            );
        }

        // First collection
        if (collections.length > 0 && (collections[0].bookUuids || []).length > 0) {
            var col = collections[0];
            var colUuids = col.bookUuids || [];
            var colCount = colUuids.length;
            var bookMap = {};
            $.each(books, function (i, b) { bookMap[b.bookUuid] = b; });
            var colCovers = '<div class="home-card-covers"><div class="home-card-cover-stack">';
            var shown = 0;
            for (var ci = 0; ci < colUuids.length && shown < 3; ci++) {
                if (bookMap[colUuids[ci]]) {
                    colCovers += homeStackCoverHtml(bookMap[colUuids[ci]]);
                    shown++;
                }
            }
            colCovers += '</div></div>';
            $grid.append(
                '<div class="col-12 col-md-6"><a href="#/collections/' + col.collectionUuid + '" class="home-card">' +
                    colCovers +
                    '<div class="home-card-title">' + escapeHtml(col.name) + '</div>' +
                    '<div class="home-card-sub">' + colCount + (colCount === 1 ? ' book' : ' books') + '</div>' +
                '</a></div>'
            );
        }
    });
}

function initBooksView() {
    var sortPrefs = getSortPrefs("books", "Recent", "Desc");
    var filterPref = getFilterPref("books", "all");
    restoreSortDropdown($("#books-sort-dropdown"), sortPrefs.sort, sortPrefs.order);
    restoreFilterDropdown($("[data-page-key='books']").first(), filterPref);

    function loadBooks() {
        var prefs = getSortPrefs("books", "Recent", "Desc");
        var filter = getFilterPref("books", "all");
        $.get("/api/alexandria/books?sort=" + prefs.sort + "&order=" + prefs.order, function (books) {
            updateMediaTypeFilters($("[data-page-key='books']").first(), books);
            if (filter === "archived") {
                books = books.filter(function (b) { return b.isArchived; });
            } else {
                books = books.filter(function (b) { return !b.isArchived; });
                books = applyMediaTypeFilter(books, filter);
                if (filter === "unread") {
                    books = books.filter(function (b) { return !b.readingProgress || b.readingProgress.status === "ReadyToRead"; });
                } else if (filter === "reading") {
                    books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Reading"; });
                } else if (filter === "finished") {
                    books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Finished"; });
                }
            }
            renderBooks(books);
        });
    }

    pageReload = loadBooks;
    loadBooks();
}

function initAuthorsView() {
    var sortPrefs = getSortPrefs("authors", "LastName", "Asc");
    restoreSortDropdown($("#authors-sort-dropdown"), sortPrefs.sort, sortPrefs.order);

    function loadAuthors() {
        var prefs = getSortPrefs("authors", "LastName", "Asc");
        $.get("/api/alexandria/authors?sort=" + prefs.sort + "&order=" + prefs.order, function (authors) {
            var $list = $("#author-list").empty();
            $.each(authors, function (i, author) {
                $list.append(
                    '<a href="#/authors/' + author.authorUuid + '" class="list-group-item list-group-item-action d-flex justify-content-between align-items-center px-0">' +
                        '<span class="fw-semibold text-body">' + escapeHtml(author.name) + '</span>' +
                        '<span class="text-body-secondary">' + escapeHtml(bookCountText(author.books || [])) + '</span>' +
                    '</a>'
                );
            });
        });
    }

    pageReload = loadAuthors;
    loadAuthors();
}

function initAuthorDetailView(authorUuid) {
    if (!authorUuid) { window.location.hash = "#/authors"; return; }

    var sortPrefs = getSortPrefs("author-detail", "Title", "Asc");
    var filterPref = getFilterPref("author-detail", "all");
    restoreSortDropdown($("#detail-sort-dropdown"), sortPrefs.sort, sortPrefs.order);
    restoreFilterDropdown($("[data-page-key='author-detail']").first(), filterPref);

    var currentAuthor = null;

    function loadAuthor() {
        var prefs = getSortPrefs("author-detail", "Title", "Asc");
        var filter = getFilterPref("author-detail", "all");
        $.get("/api/alexandria/authors/" + authorUuid + "?sort=" + prefs.sort + "&order=" + prefs.order, function (author) {
            currentAuthor = author;
            $("#detail-name").text(author.name);
            document.title = "Alexandria - " + author.name;

            var books = author.books || [];
            updateMediaTypeFilters($("[data-page-key='author-detail']").first(), books);
            books = applyMediaTypeFilter(books, filter);
            if (filter === "unread") {
                books = books.filter(function (b) { return !b.readingProgress || b.readingProgress.status === "ReadyToRead"; });
            } else if (filter === "reading") {
                books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Reading"; });
            } else if (filter === "finished") {
                books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Finished"; });
            }
            renderBooks(books);
        });
    }

    $("#edit-entity-btn").on("click", function (e) {
        e.preventDefault();
        if (currentAuthor)
            openEntityEditModal("author", currentAuthor);
    });

    pageReload = loadAuthor;
    loadAuthor();
}

function initSeriesView() {
    var sortPrefs = getSortPrefs("series", "SeriesTitle", "Asc");
    restoreSortDropdown($("#series-sort-dropdown"), sortPrefs.sort, sortPrefs.order);

    function loadSeries() {
        var prefs = getSortPrefs("series", "SeriesTitle", "Asc");
        $.get("/api/alexandria/series?sort=" + prefs.sort + "&order=" + prefs.order, function (seriesList) {
            var $grid = $("#series-grid").empty();
            var $list = $("#series-list").empty();
            $.each(seriesList, function (i, s) {
                var countText = bookCountText(s.books || []);
                var firstBook = s.books && s.books.length > 0 ? s.books[0] : null;
                var authors = firstBook ? (firstBook.authors || []).map(function(a){return a.name}).join(", ") : "";

                if (firstBook) {
                    var detailUrl = "#/series/" + s.seriesUuid;
                    $grid.append(
                        '<div class="col"><a href="' + detailUrl + '" class="book-card">' +
                            bookCoverHtml(firstBook, "book-cover-img") +
                            '<div class="book-status">' + escapeHtml(countText) + '</div>' +
                        '</a></div>'
                    );
                    $list.append(
                        '<a href="' + detailUrl + '" class="series-list-item">' +
                            '<img loading="lazy" src="' + coverUrl(firstBook.bookUuid, null, null, firstBook.modifiedAt) + '" class="book-list-cover-img" alt="" onerror="this.style.display=\'none\'">' +
                            '<div class="series-list-info">' +
                                '<div class="series-list-title">' + escapeHtml(s.name) + '</div>' +
                                '<div class="series-list-author">' + escapeHtml(authors) + '</div>' +
                                '<div class="series-list-count">' + escapeHtml(countText) + '</div>' +
                            '</div>' +
                        '</a>'
                    );
                }
            });
        });
    }

    pageReload = loadSeries;
    loadSeries();
}

function initSeriesDetailView(uuid) {
    if (!uuid) { window.location.hash = "#/series"; return; }

    var sortPrefs = getSortPrefs("series-detail", "BookNumber", "Asc");
    var filterPref = getFilterPref("series-detail", "all");
    restoreSortDropdown($("#series-detail-sort-dropdown"), sortPrefs.sort, sortPrefs.order);
    restoreFilterDropdown($("[data-page-key='series-detail']").first(), filterPref);

    var currentSeries = null;

    function loadSeries() {
        var prefs = getSortPrefs("series-detail", "BookNumber", "Asc");
        var filter = getFilterPref("series-detail", "all");
        $.get("/api/alexandria/series/" + uuid + "?sort=" + prefs.sort + "&order=" + prefs.order, function (series) {
            currentSeries = series;
            $("#series-name").text(series.name);
            document.title = "Alexandria - " + series.name;

            var books = series.books || [];
            updateMediaTypeFilters($("[data-page-key='series-detail']").first(), books);
            books = applyMediaTypeFilter(books, filter);
            if (filter === "unread") {
                books = books.filter(function (b) { return !b.readingProgress || b.readingProgress.status === "ReadyToRead"; });
            } else if (filter === "reading") {
                books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Reading"; });
            } else if (filter === "finished") {
                books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Finished"; });
            }
            renderBooks(books);
        });
    }

    $("#edit-entity-btn").on("click", function (e) {
        e.preventDefault();
        if (currentSeries)
            openEntityEditModal("series", currentSeries);
    });

    pageReload = loadSeries;
    loadSeries();
}

function initCollectionsView() {
    var sortPrefs = getSortPrefs("collections", "CollectionName", "Asc");
    restoreSortDropdown($("#collections-sort-dropdown"), sortPrefs.sort, sortPrefs.order);

    function loadCollections() {
        var prefs = getSortPrefs("collections", "CollectionName", "Asc");
        $.get("/api/alexandria/collections?sort=" + prefs.sort + "&order=" + prefs.order, function (collections) {
            var $list = $("#collection-list").empty();
            $.each(collections, function (i, c) {
                var parts = [];
                if (c.ebookCount > 0) parts.push(c.ebookCount + (c.ebookCount === 1 ? " book" : " books"));
                if (c.audiobookCount > 0) parts.push(c.audiobookCount + (c.audiobookCount === 1 ? " audiobook" : " audiobooks"));
                var countText = parts.join(", ") || "0 books";
                $list.append(
                    '<a href="#/collections/' + c.collectionUuid + '" class="list-group-item list-group-item-action d-flex justify-content-between align-items-center px-0">' +
                        '<span class="fw-semibold text-body">' + escapeHtml(c.name) + '</span>' +
                        '<span class="text-body-secondary">' + escapeHtml(countText) + '</span>' +
                    '</a>'
                );
            });
        });
    }

    pageReload = loadCollections;
    loadCollections();
}

function initCollectionDetailView(uuid) {
    if (!uuid) { window.location.hash = "#/collections"; return; }

    var sortPrefs = getSortPrefs("collection-detail", "Title", "Asc");
    var filterPref = getFilterPref("collection-detail", "all");
    restoreSortDropdown($("#detail-sort-dropdown"), sortPrefs.sort, sortPrefs.order);
    restoreFilterDropdown($("[data-page-key='collection-detail']").first(), filterPref);

    function loadCollection() {
        var prefs = getSortPrefs("collection-detail", "Title", "Asc");
        var filter = getFilterPref("collection-detail", "all");
        $.get("/api/alexandria/collections/" + uuid + "?sort=" + prefs.sort + "&order=" + prefs.order, function (collection) {
            $("#detail-name").text(collection.name);
            document.title = "Alexandria - " + collection.name;

            var books = collection.books || [];
            updateMediaTypeFilters($("[data-page-key='collection-detail']").first(), books);
            books = applyMediaTypeFilter(books, filter);
            if (filter === "unread") {
                books = books.filter(function (b) { return !b.readingProgress || b.readingProgress.status === "ReadyToRead"; });
            } else if (filter === "reading") {
                books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Reading"; });
            } else if (filter === "finished") {
                books = books.filter(function (b) { return b.readingProgress && b.readingProgress.status === "Finished"; });
            }
            renderBooks(books);
        });
    }

    pageReload = loadCollection;
    loadCollection();
}

var appRoutes = {
    "": { template: "view-home", title: "Alexandria", nav: "home", init: initHomeView },
    "books": { template: "view-books", title: "Alexandria - Books", nav: "books", init: initBooksView },
    "authors": { template: "view-authors", title: "Alexandria - Authors", nav: "books", init: initAuthorsView },
    "authors-detail": { template: "view-author-detail", title: "Alexandria - Author", nav: "books", init: initAuthorDetailView },
    "series": { template: "view-series", title: "Alexandria - Series", nav: "books", init: initSeriesView },
    "series-detail": { template: "view-series-detail", title: "Alexandria - Series", nav: "books", init: initSeriesDetailView },
    "collections": { template: "view-collections", title: "Alexandria - Collections", nav: "books", init: initCollectionsView },
    "collections-detail": { template: "view-collection-detail", title: "Alexandria - Collection", nav: "books", init: initCollectionDetailView },
    "devices": { template: "view-devices", title: "Alexandria - Devices", nav: "more", init: initDevicesView },
    "users": { template: "view-users", title: "Alexandria - Users", nav: "more", init: initUsersView },
    "more": { template: "view-more", title: "Alexandria - More", nav: "more", init: function () { } }
};

function resolveRoute() {
    var segments = (window.location.hash || "").replace(/^#\/?/, "").split("/").filter(function (s) { return s.length > 0; });
    var key = segments[0] || "";
    var param = segments.length > 1 ? decodeURIComponent(segments[1]) : null;
    if (param && appRoutes[key + "-detail"])
        return { route: appRoutes[key + "-detail"], param: param };
    if (appRoutes[key])
        return { route: appRoutes[key], param: null };
    return { route: appRoutes[""], param: null };
}

function mountView() {
    var resolved = resolveRoute();
    pageReload = null;

    // Full page loads used to dismiss overlays on navigation; the router must do it explicitly.
    $(".book-modal-backdrop.open, .search-modal-backdrop.open, .sync-popover-backdrop.open").removeClass("open");

    var template = document.getElementById(resolved.route.template);
    var $root = $("#view-root");
    $root.empty().append(template.content.cloneNode(true));

    $root.find(".dropdown-item[data-sort]").append(' <span class="sort-arrow"></span>');
    applySavedViewMode();

    $(".bottom-nav a").removeClass("active").filter("[data-nav='" + resolved.route.nav + "']").addClass("active");
    document.title = resolved.route.title;
    window.scrollTo(0, 0);

    resolved.route.init(resolved.param);
}

$(function () {
    mountView();
    $(window).on("hashchange", mountView);
});

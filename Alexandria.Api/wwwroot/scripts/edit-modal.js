// ===== Edit Modal =====
function openEditModal(uuid) {
    $.get("/api/alexandria/books/" + uuid, function (book) {
        currentModalUuid = book.bookUuid;
        if (!originalModalHtml)
            originalModalHtml = $("#book-modal").html();

        var authors = (book.authors || []).map(function(a){return a.name}).join(", ");
        var coverSrc = coverUrl(book.bookUuid, COVER_LARGE.w, COVER_LARGE.h);

        $("#book-modal").html(
            '<div class="book-modal-title-bar">Edit Book</div>' +
            '<button class="book-modal-close" id="bm-edit-close">&times;</button>' +
            '<div class="bm-edit-form">' +
                '<div class="bm-edit-cover-col">' +
                    '<div class="cover-frame book-modal-cover" style="cursor:pointer;position:relative;"><img id="bm-edit-cover-img" src="' + coverSrc + '" alt="" onerror="this.style.display=\'none\';this.parentElement.querySelector(\'.book-cover\').style.display=\'flex\';">' +
                    '<div class="book-cover" style="display:none;background:' + fallbackColor(book.bookUuid) + ';">' + escapeHtml(book.title) + '</div>' +
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
                            '<input type="text" class="bm-edit-input" id="bm-edit-series-name" value="' + escapeHtml(book.series ? book.series.name : "") + '">' +
                        '</div>' +
                        '<div class="bm-edit-field" style="width:100px;">' +
                            '<div class="bm-edit-label">Series Index</div>' +
                            '<input type="number" step="any" class="bm-edit-input" id="bm-edit-series-index" value="' + (book.series && book.series.index != null ? book.series.index : "") + '">' +
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
        index: seriesIdx !== "" ? parseFloat(seriesIdx) : null,
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
            if (pageReload) { pageReload(); }
            openEditModal(uuid);
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
            bustCoverCache(uuid);
            $("#bm-edit-cover-img").attr("src", coverUrl(uuid, COVER_LARGE.w, COVER_LARGE.h)).show().next().hide();
            if (pageReload) { pageReload(); }
        },
        complete: function () { hideEditLoading(".bm-edit-cover-upload span"); }
    });
}

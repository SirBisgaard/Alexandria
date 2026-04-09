// ===== Edit Modal =====
var comboAuthors = [];
var comboSeries = [];
var comboPublishers = [];
var comboEntity = [];
var comboHighlight = -1;

var FIELD_MAP = {
    "Title": "bm-edit-title",
    "Authors": "bm-edit-authors",
    "Language": "bm-edit-language",
    "PublicationDate": "bm-edit-pub-date",
    "Index": "bm-edit-series-index"
};

function clearEditErrors() {
    $(".bm-edit-error").remove();
    $(".bm-edit-input-invalid").removeClass("bm-edit-input-invalid");
    $("#bm-edit-error-banner").hide().text("");
}

function showFieldError(fieldId, message) {
    var $input = $("#" + fieldId);
    $input.addClass("bm-edit-input-invalid");
    $input.closest(".bm-edit-field").append('<div class="bm-edit-error">' + escapeHtml(message) + '</div>');
}

function showBannerError(message) {
    $("#bm-edit-error-banner").text(message).show();
}

function handleMetadataError(jqXHR) {
    clearEditErrors();
    if (jqXHR.status === 400) {
        try {
            var body = typeof jqXHR.responseJSON === "object" ? jqXHR.responseJSON : JSON.parse(jqXHR.responseText);
            if (body && body.errors) {
                for (var key in body.errors) {
                    var fieldId = FIELD_MAP[key];
                    var messages = body.errors[key];
                    if (fieldId && messages && messages.length > 0)
                        showFieldError(fieldId, messages[0]);
                }
                return;
            }
        } catch (e) { }
        showBannerError(jqXHR.responseText || "Invalid request.");
    } else {
        showBannerError("Something went wrong. Please try again.");
    }
}

function handleCoverError(jqXHR) {
    clearEditErrors();
    if (jqXHR.status === 400) {
        var message = jqXHR.responseText || "Cover upload failed.";
        if (message.length >= 2 && message.charAt(0) === '"' && message.charAt(message.length - 1) === '"') {
            try { message = JSON.parse(message); } catch (e) { }
        }
        showBannerError(message);
    } else {
        showBannerError("Cover upload failed. Please try again.");
    }
}

function openEditModal(uuid) {
    $.when(
        $.get("/api/alexandria/books/" + uuid),
        $.get("/api/alexandria/authors"),
        $.get("/api/alexandria/series"),
        $.get("/api/alexandria/publishers")
    ).done(function (bookRes, authorsRes, seriesRes, publishersRes) {
        var book = bookRes[0];
        comboAuthors = (authorsRes[0] || []).map(function (a) { return a.name; });
        comboSeries = (seriesRes[0] || []).map(function (s) { return s.name; });
        comboPublishers = publishersRes[0] || [];

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
                    '<div class="book-cover" style="display:none;background:' + fallbackColor(book.bookUuid) + ';">' + escapeHtml(book.title) + '<small>' + escapeHtml(authors) + '</small></div>' +
                    (book.hasCover ? '<button class="bm-edit-cover-remove" id="bm-edit-cover-remove" title="Remove cover">&times;</button>' : '') + '</div>' +
                    '<label class="bm-edit-btn bm-edit-cover-upload">' +
                        '<input type="file" id="bm-edit-cover-file" accept="image/*" style="display:none;">' +
                        '<span>Upload Cover</span>' +
                    '</label>' +
                    (book.mediaType === "Audiobook" ? '<div class="bm-edit-field" style="margin-top:12px;"><div class="bm-edit-label">Is Abridged</div><label class="bm-edit-checkbox"><input type="checkbox" id="bm-edit-abridged"' + (book.isAbridged ? ' checked' : '') + '><span class="bm-edit-checkmark"></span></label></div>' : '') +
                '</div>' +
                '<div class="bm-edit-fields">' +
                    '<div class="bm-edit-field">' +
                        '<div class="bm-edit-label">Title</div>' +
                        '<input type="text" class="bm-edit-input" id="bm-edit-title" value="' + escapeHtml(book.title) + '">' +
                    '</div>' +
                    '<div class="bm-edit-field">' +
                        '<div class="bm-edit-label">Author(s) <span class="bm-edit-hint">Separate multiple authors with commas</span></div>' +
                        '<div class="bm-combo-wrap">' +
                            '<input type="text" class="bm-edit-input bm-combo-input" id="bm-edit-authors" data-combo="authors" autocomplete="off" value="' + escapeHtml(authors) + '">' +
                            '<div class="bm-combo-dropdown" id="bm-combo-authors"></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="bm-edit-row">' +
                        '<div class="bm-edit-field" style="width:120px;">' +
                            '<div class="bm-edit-label">Language</div>' +
                            '<select class="bm-edit-input" id="bm-edit-language">' + languageOptions(book.language) + '</select>' +
                        '</div>' +
                        '<div class="bm-edit-field" style="flex:1;">' +
                            '<div class="bm-edit-label">Publisher</div>' +
                            '<div class="bm-combo-wrap">' +
                                '<input type="text" class="bm-edit-input bm-combo-input" id="bm-edit-publisher" data-combo="publishers" autocomplete="off" value="' + escapeHtml(book.publisher || "") + '">' +
                                '<div class="bm-combo-dropdown" id="bm-combo-publishers"></div>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="bm-edit-row">' +
                        '<div class="bm-edit-field" style="flex:1;">' +
                            '<div class="bm-edit-label">Series Name</div>' +
                            '<div class="bm-combo-wrap">' +
                                '<input type="text" class="bm-edit-input bm-combo-input" id="bm-edit-series-name" data-combo="series" autocomplete="off" value="' + escapeHtml(book.series ? book.series.name : "") + '">' +
                                '<div class="bm-combo-dropdown" id="bm-combo-series"></div>' +
                            '</div>' +
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
            '<div class="bm-edit-error-banner" id="bm-edit-error-banner"></div>' +
            '<div class="bm-edit-footer">' +
                '<div class="bm-edit-warning">Editing metadata or covers will trigger a re-sync to the device on next sync. The book will need to be re-downloaded on the device, but reading progress is preserved. A device reboot may be needed to refresh the cover in Currently Reading.</div>' +
                '<div class="bm-edit-buttons"><button class="bm-edit-btn" id="bm-edit-cancel">Cancel</button>' +
                '<button class="bm-edit-btn bm-edit-btn-primary" id="bm-edit-save">Save</button></div>' +
            '</div>'
        );

        $("#book-modal-backdrop").addClass("open");
    });
}

function showEditLoading(target) {
    closeAllComboDropdowns();
    var $el = $(target);
    $el.data("original-text", $el.text()).text("Loading...").prop("disabled", true);
    $(".bm-edit-btn").prop("disabled", true).addClass("bm-edit-btn-disabled");
    $(".bm-edit-input, .bm-edit-textarea, #bm-edit-abridged").prop("disabled", true).addClass("bm-edit-input-disabled");
}

function hideEditLoading(target) {
    var $el = $(target);
    $el.text($el.data("original-text") || "").prop("disabled", false);
    $(".bm-edit-btn").prop("disabled", false).removeClass("bm-edit-btn-disabled");
    $(".bm-edit-input, .bm-edit-textarea, #bm-edit-abridged").prop("disabled", false).removeClass("bm-edit-input-disabled");
}

function saveBookMetadata(uuid) {
    var seriesIdx = $("#bm-edit-series-index").val();
    var $abridged = $("#bm-edit-abridged");
    var book = {
        title: $("#bm-edit-title").val(),
        authors: $("#bm-edit-authors").val().split(",").map(function (a) { return a.trim(); }).filter(function (a) { return a; }),
        language: $("#bm-edit-language").val(),
        publisher: $("#bm-edit-publisher").val() || null,
        seriesName: $("#bm-edit-series-name").val() || null,
        index: seriesIdx !== "" ? parseFloat(seriesIdx) : null,
        publicationDate: $("#bm-edit-pub-date").val() || null,
        description: $("#bm-edit-description").val() || null,
        isAbridged: $abridged.length ? $abridged.is(":checked") : null
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
        error: function (jqXHR) { handleMetadataError(jqXHR); },
        complete: function () { hideEditLoading("#bm-edit-save"); }
    });
}

function closeAllComboDropdowns() {
    $(".bm-combo-dropdown").hide().empty();
    comboHighlight = -1;
}

function updateComboDropdown($input) {
    var comboType = $input.data("combo");
    var allItems = comboType === "authors" ? comboAuthors : comboType === "publishers" ? comboPublishers : comboType === "entity" ? comboEntity : comboSeries;
    var $dropdown = $input.siblings(".bm-combo-dropdown");
    var val = $input.val();

    var segment, usedNames;
    if (comboType === "authors") {
        var lastComma = val.lastIndexOf(",");
        segment = (lastComma >= 0 ? val.substring(lastComma + 1) : val).trim().toLowerCase();
        usedNames = val.split(",").map(function (s) { return s.trim().toLowerCase(); }).filter(function (s) { return s; });
    } else {
        segment = val.trim().toLowerCase();
        usedNames = [];
    }

    var matches = [];
    for (var i = 0; i < allItems.length && matches.length < 8; i++) {
        var name = allItems[i];
        var nameLower = name.toLowerCase();
        if (comboType === "authors" && usedNames.indexOf(nameLower) >= 0)
            continue;
        if (segment === "" || nameLower.indexOf(segment) >= 0)
            matches.push(name);
    }

    comboHighlight = -1;
    if (matches.length === 0 || (matches.length === 1 && matches[0].toLowerCase() === segment)) {
        $dropdown.hide().empty();
        return;
    }

    var html = "";
    for (var j = 0; j < matches.length; j++)
        html += '<div class="bm-combo-item" data-value="' + escapeHtml(matches[j]) + '">' + escapeHtml(matches[j]) + '</div>';

    $dropdown.html(html).show();
}

function selectComboItem($input, value) {
    if ($input.data("combo") === "authors") {
        var val = $input.val();
        var lastComma = val.lastIndexOf(",");
        var prefix = lastComma >= 0 ? val.substring(0, lastComma + 1) + " " : "";
        $input.val(prefix + value + ", ");
    } else {
        $input.val(value);
    }
    $input.siblings(".bm-combo-dropdown").hide().empty();
    comboHighlight = -1;
    $input.focus();
}

function uploadBookCover(uuid, file, previousSrc) {
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
        error: function (jqXHR) {
            if (previousSrc)
                $("#bm-edit-cover-img").attr("src", previousSrc);
            else
                $("#bm-edit-cover-img").hide().next().show();
            handleCoverError(jqXHR);
        },
        complete: function () { hideEditLoading(".bm-edit-cover-upload span"); }
    });
}

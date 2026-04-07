// ===== Entity Edit Modal (Author / Series) =====
var entityEditType = null;
var entityEditData = null;

function openEntityEditModal(type, entity) {
    entityEditType = type;
    entityEditData = entity;

    var label = type === "author" ? "Author" : "Series";
    var name = entity.name || "";
    var books = entity.books || [];

    var bookListHtml = "";
    for (var i = 0; i < books.length; i++) {
        var b = books[i];
        var coverSrc = b.hasCover ? coverUrl(b.bookUuid, COVER_SMALL.w, COVER_SMALL.h) : "";
        bookListHtml +=
            '<div class="entity-edit-book-item">' +
                '<div class="cover-frame entity-edit-book-cover">' +
                    (coverSrc ? '<img loading="lazy" src="' + coverSrc + '" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' : '') +
                    '<div class="book-cover" style="' + (coverSrc ? 'display:none;' : '') + 'background:' + fallbackColor(b.bookUuid) + ';">' + escapeHtml(b.title) + '</div>' +
                '</div>' +
                '<div class="entity-edit-book-info">' +
                    '<div class="entity-edit-book-title">' + escapeHtml(b.title) + '</div>' +
                    (type === "series" && b.authors && b.authors.length ? '<div class="entity-edit-book-author">' + escapeHtml(b.authors.map(function(a){return a.name}).join(", ")) + '</div>' : '') +
                '</div>' +
            '</div>';
    }

    var html =
        '<div class="book-modal-backdrop open" id="entity-edit-modal-backdrop">' +
            '<div class="book-modal" id="entity-edit-modal">' +
                '<div class="book-modal-title-bar">Edit ' + label + '</div>' +
                '<button class="book-modal-close" id="entity-edit-close">&times;</button>' +
                '<div class="bm-edit-field" style="margin-bottom:16px;">' +
                    '<div class="bm-edit-label">Name</div>' +
                    '<input type="text" class="bm-edit-input" id="entity-edit-name" value="' + escapeHtml(name) + '">' +
                '</div>' +
                '<div class="bm-edit-error-banner" id="entity-edit-error-banner"></div>' +
                (books.length > 0 ?
                    '<div class="entity-edit-book-list-header">Books (' + books.length + ')</div>' +
                    '<div class="entity-edit-book-list">' + bookListHtml + '</div>' : '') +
                '<div class="bm-edit-footer">' +
                    '<div class="bm-edit-warning">Renaming will update metadata in all associated books and trigger a re-sync to the device.</div>' +
                    '<div class="bm-edit-buttons">' +
                        '<button class="bm-edit-btn" id="entity-edit-cancel">Cancel</button>' +
                        '<button class="bm-edit-btn bm-edit-btn-primary" id="entity-edit-save">Save</button>' +
                    '</div>' +
                '</div>' +
            '</div>' +
        '</div>';

    $("#entity-edit-modal-backdrop").remove();
    $("body").append(html);
}

function closeEntityEditModal() {
    $("#entity-edit-modal-backdrop").removeClass("open");
    setTimeout(function () { $("#entity-edit-modal-backdrop").remove(); }, 200);
    entityEditType = null;
    entityEditData = null;
}

function clearEntityEditErrors() {
    $("#entity-edit-modal .bm-edit-error").remove();
    $("#entity-edit-modal .bm-edit-input-invalid").removeClass("bm-edit-input-invalid");
    $("#entity-edit-error-banner").hide().text("");
}

function showEntityEditFieldError(message) {
    var $input = $("#entity-edit-name");
    $input.addClass("bm-edit-input-invalid");
    $input.closest(".bm-edit-field").append('<div class="bm-edit-error">' + escapeHtml(message) + '</div>');
}

function handleEntityEditError(jqXHR) {
    clearEntityEditErrors();
    if (jqXHR.status === 400 || jqXHR.status === 409) {
        try {
            var body = typeof jqXHR.responseJSON === "object" ? jqXHR.responseJSON : JSON.parse(jqXHR.responseText);
            if (body && body.errors && body.errors.Name && body.errors.Name.length > 0) {
                showEntityEditFieldError(body.errors.Name[0]);
                return;
            }
        } catch (e) { }
        $("#entity-edit-error-banner").text(jqXHR.responseText || "Invalid request.").show();
    } else {
        $("#entity-edit-error-banner").text("Something went wrong. Please try again.").show();
    }
}

function saveEntityEdit() {
    var newName = $("#entity-edit-name").val().trim();
    clearEntityEditErrors();

    if (!newName) {
        showEntityEditFieldError("Name is required.");
        return;
    }

    var url, successHandler;
    if (entityEditType === "author") {
        url = "/api/alexandria/authors/" + entityEditData.authorUuid;
        successHandler = function () {
            closeEntityEditModal();
            if (pageReload) { pageReload(); }
        };
    } else {
        url = "/api/alexandria/series/" + entityEditData.seriesUuid;
        successHandler = function () {
            closeEntityEditModal();
            if (pageReload) { pageReload(); }
        };
    }

    var $save = $("#entity-edit-save");
    $save.data("original-text", $save.text()).text("Loading...").prop("disabled", true);
    $(".bm-edit-btn").prop("disabled", true).addClass("bm-edit-btn-disabled");
    $("#entity-edit-name").prop("disabled", true).addClass("bm-edit-input-disabled");

    $.ajax({
        url: url,
        method: "PUT",
        contentType: "application/json",
        data: JSON.stringify({ name: newName }),
        success: successHandler,
        error: function (jqXHR) { handleEntityEditError(jqXHR); },
        complete: function () {
            $save.text($save.data("original-text") || "Save").prop("disabled", false);
            $(".bm-edit-btn").prop("disabled", false).removeClass("bm-edit-btn-disabled");
            $("#entity-edit-name").prop("disabled", false).removeClass("bm-edit-input-disabled");
        }
    });
}

$(document).on("click", "#entity-edit-close", closeEntityEditModal);
$(document).on("click", "#entity-edit-cancel", closeEntityEditModal);
$(document).on("click", "#entity-edit-save", saveEntityEdit);

$(document).on("click", "#entity-edit-modal-backdrop", function (e) {
    if (e.target === this) { closeEntityEditModal(); }
});

$(document).on("input", "#entity-edit-name", function () {
    $(this).removeClass("bm-edit-input-invalid");
    $(this).closest(".bm-edit-field").find(".bm-edit-error").remove();
    $("#entity-edit-error-banner").hide();
});

$(document).on("keydown", "#entity-edit-name", function (e) {
    if (e.key === "Enter") {
        e.preventDefault();
        saveEntityEdit();
    }
});

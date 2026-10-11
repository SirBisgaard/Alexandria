// ===== Settings Modal =====
var m4bDescriptions = {
    "Nothing": "Stream M4B chapters on demand. Chapters are transcoded to MP3 in real-time when the Kobo requests them. Long chapters risk timeout.",
    "TranscodeAtIndex": "Pre-transcode each chapter to MP3 during library indexing. Ensures transfers complete at the cost of additional disk space.",
    "ConvertAtIndex": "Convert the entire M4B into an MP3 folder during indexing and delete the original M4B file when done. Best compatibility with Kobo devices and uses the same disk space."
};

var COMIC_RENDERING_FIELDS = [
    {
        key: "spreadHandling", label: "Double-page spreads", type: "select",
        options: [["Rotate", "Rotate"], ["Split", "Split"], ["SplitAndRotate", "Split and rotate"]],
        desc: "Rotate turns a spread a quarter turn (right edge up) so it fills the screen when you turn the device. Split cuts it into its two pages in reading order; very wide panoramas are still rotated. Split and rotate shows both."
    },
    {
        key: "marginCropping", label: "Margin cropping", type: "select",
        options: [["Margins", "Crop margins"], ["Off", "Off"]],
        desc: "Crop empty borders around the art so pages show larger. Pages where the art runs to the edge are left alone."
    },
    {
        key: "croppingStrength", label: "Cropping strength", type: "select",
        options: [["Gentle", "Gentle"], ["Normal", "Normal"], ["Aggressive", "Aggressive"]],
        desc: "Gentle only crops plain, even borders. Aggressive also crops borders with texture or faint marks, at a higher risk of trimming faint artwork."
    },
    { key: "preserveMarginPercent", label: "Preserve margin %", type: "number", min: 0, max: 10, step: 0.5, desc: "How much border to keep around the art after cropping. Default is 1 %." },
    { key: "jpegQuality", label: "Image quality", type: "number", min: 1, max: 100, step: 1, desc: "JPEG quality of the pages (1–100). Higher is sharper but makes larger files. Default is 85." },
    {
        key: "autoContrast", label: "Auto-contrast", type: "select",
        options: [["BlackAndWhiteOnly", "Black & white pages"], ["All", "All pages"], ["Off", "Off"]],
        desc: "Stretch pages to full black and white for crisper e-ink. Pages with deliberately low contrast are left alone."
    }
];

function comicRenderingTilesHtml() {
    var html = '<div class="settings-tile">' +
        '<div class="d-flex align-items-center gap-3">' +
            '<div class="flex-grow-1">' +
                '<div class="settings-tile-label">Comic rendering</div>' +
                '<div class="settings-tile-desc">How comics are turned into pages for your Kobo. Changing any of these rebuilds every comic on the next library sync.</div>' +
            '</div>' +
            '<button class="bm-edit-btn" data-reset-comic-rendering>Reset to defaults</button>' +
        '</div>' +
    '</div>';
    for (var i = 0; i < COMIC_RENDERING_FIELDS.length; i++) {
        var field = COMIC_RENDERING_FIELDS[i];
        var input;
        if (field.type === "select") {
            input = '<select class="bm-edit-input" data-comic-setting="' + field.key + '">';
            for (var j = 0; j < field.options.length; j++)
                input += '<option value="' + field.options[j][0] + '">' + field.options[j][1] + '</option>';
            input += '</select>';
        } else {
            input = '<input type="number" class="bm-edit-input" data-comic-setting="' + field.key + '" min="' + field.min + '" max="' + field.max + '" step="' + field.step + '">';
        }
        html += '<div class="settings-tile">' +
            '<div class="settings-tile-label">' + field.label + '</div>' +
            input +
            '<div class="settings-tile-desc">' + field.desc + '</div>' +
            '<div class="settings-tile-error"></div>' +
        '</div>';
    }
    return html;
}

function setComicRenderingInputs($c, values) {
    if (!values)
        return;
    for (var i = 0; i < COMIC_RENDERING_FIELDS.length; i++) {
        var key = COMIC_RENDERING_FIELDS[i].key;
        $c.find("[data-comic-setting='" + key + "']").val(values[key]);
    }
}

function readComicRenderingInputs($c) {
    var values = {};
    for (var i = 0; i < COMIC_RENDERING_FIELDS.length; i++) {
        var field = COMIC_RENDERING_FIELDS[i];
        var raw = $c.find("[data-comic-setting='" + field.key + "']").val();
        values[field.key] = field.type === "number" ? parseFloat(raw) : raw;
    }
    return values;
}

function showSettingsTab($c, tab) {
    $c.find("[data-settings-tab]").removeClass("active").filter("[data-settings-tab='" + tab + "']").addClass("active");
    $c.find("[data-settings-pane]").hide().filter("[data-settings-pane='" + tab + "']").show();
}

function updateM4bDescription(container) {
    var $c = $(container);
    var val = $c.find("[data-setting='m4bFileTreatment']").val();
    $c.find(".settings-m4b-desc").text(m4bDescriptions[val] || "");
}

function loadSettings(container) {
    var $c = $(container);
    $c.find(".settings-error-banner").hide().text("");
    $c.find(".settings-tile-error").text("");
    $c.find(".bm-edit-input").removeClass("bm-edit-input-invalid");
    showSettingsTab($c, "general");

    $.get("/api/alexandria/settings", function (data) {
        $c.find("[data-setting='syncBatchSize']").val(data.syncBatchSize);
        $c.find("[data-setting='wordsPerPage']").val(data.wordsPerPage);
        $c.find("[data-setting='proxyLoggingEnabled']").prop("checked", data.proxyLoggingEnabled);
        $c.find("[data-setting='syncLibraryAtBoot']").prop("checked", data.syncLibraryAtBoot);
        $c.find("[data-setting='m4bFileTreatment']").val(data.m4BFileTreatment);
        $c.find("[data-setting='fileRenameTemplate']").val(data.fileRenameTemplate || "");
        setComicRenderingInputs($c, data.comicRendering);
        $c.data("comic-rendering-defaults", data.comicRenderingDefaults);
        updateM4bDescription(container);
    });
}

function saveSettings(container) {
    var $c = $(container);
    $c.find(".settings-error-banner").hide().text("");
    $c.find(".settings-tile-error").text("");
    $c.find(".bm-edit-input").removeClass("bm-edit-input-invalid");

    var payload = {
        syncBatchSize: parseInt($c.find("[data-setting='syncBatchSize']").val(), 10),
        wordsPerPage: parseInt($c.find("[data-setting='wordsPerPage']").val(), 10),
        proxyLoggingEnabled: $c.find("[data-setting='proxyLoggingEnabled']").prop("checked"),
        syncLibraryAtBoot: $c.find("[data-setting='syncLibraryAtBoot']").prop("checked"),
        m4BFileTreatment: $c.find("[data-setting='m4bFileTreatment']").val(),
        fileRenameTemplate: $c.find("[data-setting='fileRenameTemplate']").val(),
        comicRendering: readComicRenderingInputs($c)
    };

    var $btns = $c.find(".bm-edit-btn");
    var $inputs = $c.find(".bm-edit-input, .settings-toggle input");
    $btns.prop("disabled", true).addClass("bm-edit-btn-disabled");
    $inputs.prop("disabled", true);

    $.ajax({
        url: "/api/alexandria/settings",
        type: "PUT",
        contentType: "application/json",
        data: JSON.stringify(payload),
        success: function () {
            $c.find(".settings-save-status").text("Saved").show();
            setTimeout(function () { $c.find(".settings-save-status").fadeOut(); }, 2000);
        },
        error: function (jqXHR) {
            if (jqXHR.status === 400) {
                try {
                    var body = typeof jqXHR.responseJSON === "object" ? jqXHR.responseJSON : JSON.parse(jqXHR.responseText);
                    if (body && body.errors) {
                        var fieldMap = {
                            "SyncBatchSize": "syncBatchSize",
                            "WordsPerPage": "wordsPerPage",
                            "FileRenameTemplate": "fileRenameTemplate"
                        };
                        var firstInvalidPane = null;
                        for (var key in body.errors) {
                            var setting = fieldMap[key];
                            var comicMatch = /^ComicRendering\.(\w+)$/.exec(key);
                            var $input = setting
                                ? $c.find("[data-setting='" + setting + "']")
                                : comicMatch ? $c.find("[data-comic-setting='" + comicMatch[1].charAt(0).toLowerCase() + comicMatch[1].slice(1) + "']") : $();
                            if ($input.length && body.errors[key].length > 0) {
                                $input.addClass("bm-edit-input-invalid");
                                $input.closest(".settings-tile").find(".settings-tile-error").text(body.errors[key][0]);
                                firstInvalidPane = firstInvalidPane || $input.closest("[data-settings-pane]").data("settings-pane");
                            }
                        }
                        if (firstInvalidPane)
                            showSettingsTab($c, firstInvalidPane);
                        return;
                    }
                } catch (e) { }
                $c.find(".settings-error-banner").text(jqXHR.responseText || "Invalid request.").show();
            } else {
                $c.find(".settings-error-banner").text("Something went wrong. Please try again.").show();
            }
        },
        complete: function () {
            $btns.prop("disabled", false).removeClass("bm-edit-btn-disabled");
            $inputs.prop("disabled", false);
        }
    });
}

$(function () {
    $(document).on("click", ".settings-save-btn", function () {
        var container = $(this).closest("#settings-modal, #settings-page-tiles").length
            ? "#" + $(this).closest("#settings-modal, #settings-page-tiles").attr("id")
            : "#settings-modal";
        saveSettings(container);
    });

    $(document).on("input change", ".settings-tiles .bm-edit-input", function () {
        $(this).removeClass("bm-edit-input-invalid");
        $(this).closest(".settings-tile").find(".settings-tile-error").text("");
    });

    $(document).on("change", "[data-setting='m4bFileTreatment']", function () {
        var container = $(this).closest("#settings-modal, #settings-page-tiles").length
            ? "#" + $(this).closest("#settings-modal, #settings-page-tiles").attr("id")
            : "#settings-modal";
        updateM4bDescription(container);
    });

    $(document).on("click", "[data-settings-tab]", function (e) {
        e.preventDefault();
        showSettingsTab($(this).closest("#settings-modal, #settings-page-tiles"), $(this).data("settings-tab"));
    });

    $(document).on("click", "[data-reset-comic-rendering]", function () {
        var $c = $(this).closest("#settings-modal, #settings-page-tiles");
        setComicRenderingInputs($c, $c.data("comic-rendering-defaults"));
        $c.find("[data-comic-setting]").removeClass("bm-edit-input-invalid").closest(".settings-tile").find(".settings-tile-error").text("");
    });

    $(document).on("click", "[data-clear-activity-events]", function () {
        if (!confirm("Clear all per-book activity events? This cannot be undone.")) { return; }
        var $btn = $(this);
        var original = $btn.text();
        $btn.prop("disabled", true).text("Clearing…");
        $.ajax({
            url: "/api/alexandria/books/events",
            type: "DELETE",
            success: function (data) {
                $btn.text("Cleared " + (data && data.removed != null ? data.removed : "0"));
                setTimeout(function () { $btn.prop("disabled", false).text(original); }, 4000);
            },
            error: function () {
                $btn.text("Failed");
                setTimeout(function () { $btn.prop("disabled", false).text(original); }, 4000);
            }
        });
    });

    $(document).on("click", "[data-validate-template]", function () {
        var $tile = $(this).closest(".settings-tile");
        var $result = $tile.find("[data-validate-result]");
        var template = $tile.find("[data-setting='fileRenameTemplate']").val();
        $result.text("...");
        $.ajax({
            url: "/api/alexandria/settings/preview-template",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ template: template }),
            success: function (data) {
                $result.text(data.result);
            },
            error: function (jqXHR) {
                var msg = (jqXHR.responseJSON && jqXHR.responseJSON.error) || "Invalid template.";
                $result.text(msg);
            }
        });
    });

    if ($("#settings-page-tiles").length)
        loadSettings("#settings-page-tiles");
});

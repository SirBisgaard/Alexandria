// ===== Settings Modal =====
var m4bDescriptions = {
    "Nothing": "Stream M4B chapters on demand. Chapters are transcoded to MP3 in real-time when the Kobo requests them. Long chapters risk timeout.",
    "TranscodeAtIndex": "Pre-transcode each chapter to MP3 during library indexing. Ensures transfers complete at the cost of additional disk space.",
    "ConvertAtIndex": "Convert the entire M4B into an MP3 folder during indexing and delete the original M4B file when done. Best compatibility with Kobo devices and uses the same disk space."
};

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

    $.get("/api/alexandria/settings", function (data) {
        $c.find("[data-setting='syncBatchSize']").val(data.syncBatchSize);
        $c.find("[data-setting='wordsPerPage']").val(data.wordsPerPage);
        $c.find("[data-setting='proxyLoggingEnabled']").prop("checked", data.proxyLoggingEnabled);
        $c.find("[data-setting='syncLibraryAtBoot']").prop("checked", data.syncLibraryAtBoot);
        $c.find("[data-setting='m4bFileTreatment']").val(data.m4BFileTreatment);
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
        m4BFileTreatment: $c.find("[data-setting='m4bFileTreatment']").val()
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
                            "WordsPerPage": "wordsPerPage"
                        };
                        for (var key in body.errors) {
                            var setting = fieldMap[key];
                            if (setting && body.errors[key].length > 0) {
                                var $input = $c.find("[data-setting='" + setting + "']");
                                $input.addClass("bm-edit-input-invalid");
                                $input.closest(".settings-tile").find(".settings-tile-error").text(body.errors[key][0]);
                            }
                        }
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

    if ($("#settings-page-tiles").length)
        loadSettings("#settings-page-tiles");
});

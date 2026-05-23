$(function () {
    $("body").append(
        '<div class="book-modal-backdrop" id="integrations-modal-backdrop">' +
            '<div class="book-modal" id="integrations-modal">' +
                '<div class="book-modal-title-bar">Integrations</div>' +
                '<button class="book-modal-close" id="integrations-close">&times;</button>' +
                '<div class="settings-modal-content">' +
                    '<div class="settings-tiles">' +
                        '<div class="settings-tile integration-tile" data-integration="audiobookshelf">' +
                            '<div class="integration-tile-header">' +
                                '<div class="integration-tile-name">AudioBookShelf</div>' +
                                '<div class="integration-tile-enable">' +
                                    '<span class="integration-tile-enable-label">Enable</span>' +
                                    '<label class="settings-toggle"><input type="checkbox" data-integration-field="enabled"><span class="settings-toggle-slider"></span></label>' +
                                '</div>' +
                            '</div>' +
                            '<div class="integration-tile-field">' +
                                '<div class="settings-tile-label">Server url</div>' +
                                '<input type="text" class="bm-edit-input" data-integration-field="serverUrl" placeholder="https://abs.example.com">' +
                            '</div>' +
                            '<div class="integration-tile-field">' +
                                '<div class="settings-tile-label">API token</div>' +
                                '<input type="password" class="bm-edit-input" data-integration-field="apiToken" autocomplete="off">' +
                            '</div>' +
                            '<div class="settings-tile-desc integration-tile-info">' +
                                'Audiobook progress is synced to ABS; books are matched by file paths, so both services should point to the same file library. Last update wins, and sync runs every 5 minutes.' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="settings-error-banner"></div>' +
                    '<div class="settings-footer">' +
                        '<span class="settings-footer-right">' +
                            '<span class="settings-save-status"></span>' +
                            '<button class="bm-edit-btn bm-edit-btn-primary integrations-save-btn">Save</button>' +
                        '</span>' +
                    '</div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    function setBanner(message) {
        var $banner = $("#integrations-modal .settings-error-banner");
        if (message) {
            $banner.text(message).show();
        } else {
            $banner.text("").hide();
        }
    }

    function loadAudioBookShelf() {
        var $tile = $("#integrations-modal [data-integration='audiobookshelf']");
        var $server = $tile.find("[data-integration-field='serverUrl']");
        var $token = $tile.find("[data-integration-field='apiToken']");
        var $enabled = $tile.find("[data-integration-field='enabled']");

        setBanner("");
        $server.removeClass("bm-edit-input-invalid");
        $token.removeClass("bm-edit-input-invalid");

        $.ajax({
            url: "/api/alexandria/integrations/audiobookshelf",
            type: "GET",
            success: function (data) {
                $server.val(data.serverUrl || "");
                $token.val(data.apiToken || "");
                $enabled.prop("checked", !!data.enabled);
            },
            error: function () {
                setBanner("Could not load the saved integration.");
            }
        });
    }

    $(document).on("click", "[data-open-integrations]", function (e) {
        e.preventDefault();
        $("#integrations-modal-backdrop").addClass("open");
        loadAudioBookShelf();
    });

    $(document).on("input change", "#integrations-modal [data-integration-field='serverUrl'], #integrations-modal [data-integration-field='apiToken']", function () {
        $(this).removeClass("bm-edit-input-invalid");
    });

    $(document).on("click", ".integrations-save-btn", function () {
        var $tile = $("#integrations-modal [data-integration='audiobookshelf']");
        var $server = $tile.find("[data-integration-field='serverUrl']");
        var $token = $tile.find("[data-integration-field='apiToken']");
        var $enabled = $tile.find("[data-integration-field='enabled']");
        var $status = $("#integrations-modal .settings-save-status");

        setBanner("");
        $server.removeClass("bm-edit-input-invalid");
        $token.removeClass("bm-edit-input-invalid");

        var payload = {
            enabled: $enabled.prop("checked"),
            serverUrl: $server.val(),
            apiToken: $token.val()
        };

        $.ajax({
            url: "/api/alexandria/integrations/audiobookshelf",
            type: "PUT",
            contentType: "application/json",
            data: JSON.stringify(payload),
            success: function () {
                $status.text("Saved").show();
                setTimeout(function () { $status.fadeOut(); }, 2000);
            },
            error: function (jqXHR) {
                if (jqXHR.status === 400) {
                    $server.addClass("bm-edit-input-invalid");
                    $token.addClass("bm-edit-input-invalid");
                    $enabled.prop("checked", false);
                    var msg = (jqXHR.responseJSON && jqXHR.responseJSON.error) || "Could not save the integration.";
                    setBanner(msg);
                } else {
                    setBanner("Could not save the integration.");
                }
            }
        });
    });

    $("#integrations-close").on("click", function () {
        $("#integrations-modal-backdrop").removeClass("open");
    });

    $("#integrations-modal-backdrop").on("click", function (e) {
        if (e.target === this)
            $("#integrations-modal-backdrop").removeClass("open");
    });

    $(document).on("keydown", function (e) {
        if (e.key === "Escape" && $("#integrations-modal-backdrop").hasClass("open"))
            $("#integrations-modal-backdrop").removeClass("open");
    });
});

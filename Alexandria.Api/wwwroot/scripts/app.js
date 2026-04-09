// ===== App Initialization =====
$(function () {
    // ===== Inject sort arrow spans into sort dropdown items =====
    $(".dropdown-item[data-sort]").append(' <span class="sort-arrow"></span>');

    // ===== Inject Modal HTML =====
    $("body").append(
        '<div class="book-modal-backdrop" id="book-modal-backdrop">' +
            '<div class="book-modal" id="book-modal">' +
                '<div class="book-modal-title-bar">My Books</div>' +
                '<button class="book-modal-close" id="bm-close">&times;</button>' +
                '<div class="book-modal-info">' +
                    '<div class="cover-frame book-modal-cover" id="bm-cover-frame"><img id="bm-cover" src="" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' +
                    '<div id="bm-cover-fallback" class="book-cover" style="display:none;"></div></div>' +
                    '<div class="book-modal-meta">' +
                        '<div class="book-modal-book-title" id="bm-title"></div>' +
                        '<div class="book-modal-series" id="bm-series"></div>' +
                        '<div class="book-modal-author" id="bm-author"></div>' +
                        '<div class="book-modal-status-row"><div class="book-modal-status" id="bm-status"></div><div class="dropdown" id="bm-status-menu"></div></div>' +
                    '</div>' +
                '</div>' +
                '<ul class="nav nav-underline book-modal-tabs">' +
                    '<li class="nav-item"><a class="nav-link active" id="bm-tab-synopsis" href="#">Synopsis</a></li>' +
                    '<li class="nav-item"><a class="nav-link" id="bm-tab-details" href="#">Details</a></li>' +
                    '<li class="nav-item ms-auto"><a class="nav-link" id="bm-tab-data" href="#">Data</a></li>' +
                '</ul>' +
                '<div class="book-modal-tab-content">' +
                    '<div id="bm-synopsis"></div>' +
                    '<div id="bm-details" style="display:none;"></div>' +
                    '<div id="bm-data" style="display:none;"><p class="bm-data-info">Raw book metadata as returned by the Alexandria API.</p><pre><code id="bm-data-code"></code></pre></div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    // ===== Inject About Modal HTML =====
    $("body").append(
        '<div class="book-modal-backdrop" id="about-modal-backdrop">' +
            '<div class="book-modal" id="about-modal">' +
                '<div class="book-modal-title-bar">About Alexandria</div>' +
                '<button class="book-modal-close" id="about-close">&times;</button>' +
                '<div class="about-modal-content">' +
                    '<p>Named after the ancient Library of Alexandria, Alexandria is an offline e-book library manager designed for Kobo e-readers.</p>' +
                    '<p>Set out to enable offline use of your devices, it works by acting as a transparent proxy between your Kobo and the Kobo store, intercepting sync endpoints to seamlessly inject your personal library into the standard Kobo reading experience.</p>' +
                    '<p>Your books appear alongside store purchases on your device, complete with covers, reading progress, and metadata &mdash; no sideloading or USB transfers required.</p>' +
                    '<p>Simply point your Kobo at Alexandria, drop EPUB files into the library folder, and your books will sync automatically.</p>' +
                    '<div class="about-logo">Alexandria</div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    $(document).on("click", "[data-about-alexandria]", function (e) {
        e.preventDefault();
        $("#about-modal-backdrop").addClass("open");
    });

    $("#about-close").on("click", function () {
        $("#about-modal-backdrop").removeClass("open");
    });

    $("#about-modal-backdrop").on("click", function (e) {
        if (e.target === this) { $("#about-modal-backdrop").removeClass("open"); }
    });

    // ===== Inject Settings Modal HTML =====
    $("body").append(
        '<div class="book-modal-backdrop" id="settings-modal-backdrop">' +
            '<div class="book-modal" id="settings-modal">' +
                '<div class="book-modal-title-bar">Settings</div>' +
                '<button class="book-modal-close" id="settings-close">&times;</button>' +
                '<div class="settings-modal-content">' +
                    '<div class="settings-tiles">' +
                        '<div class="settings-tile">' +
                            '<div class="settings-tile-label">Sync batch size</div>' +
                            '<input type="number" class="bm-edit-input" data-setting="syncBatchSize" min="1" max="1000">' +
                            '<div class="settings-tile-desc">Number of books sent to your Kobo per sync request. Default is 60.</div>' +
                            '<div class="settings-tile-error"></div>' +
                        '</div>' +
                        '<div class="settings-tile">' +
                            '<div class="settings-tile-label">Words per page</div>' +
                            '<input type="number" class="bm-edit-input" data-setting="wordsPerPage" min="1" max="1000">' +
                            '<div class="settings-tile-desc">Used to estimate page counts for reading statistics. Default is 250.</div>' +
                            '<div class="settings-tile-error"></div>' +
                        '</div>' +
                        '<div class="settings-tile">' +
                            '<div class="settings-tile-toggle-row">' +
                                '<div class="settings-tile-label">Proxy traffic logging</div>' +
                                '<label class="settings-toggle"><input type="checkbox" data-setting="proxyLoggingEnabled"><span class="settings-toggle-slider"></span></label>' +
                            '</div>' +
                            '<div class="settings-tile-desc">Log all Kobo proxy traffic to the database for debugging.</div>' +
                        '</div>' +
                        '<div class="settings-tile">' +
                            '<div class="settings-tile-toggle-row">' +
                                '<div class="settings-tile-label">Disable Kobo store</div>' +
                                '<label class="settings-toggle"><input type="checkbox" data-setting="koboStoreKillSwitch"><span class="settings-toggle-slider"></span></label>' +
                            '</div>' +
                            '<div class="settings-tile-desc">Block all Kobo store requests. Your device will only show books from your Alexandria library.</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="settings-error-banner"></div>' +
                    '<div class="settings-footer">' +
                        '<span class="settings-save-status"></span>' +
                        '<button class="bm-edit-btn bm-edit-btn-primary settings-save-btn">Save</button>' +
                    '</div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    $(document).on("click", "[data-open-settings]", function (e) {
        e.preventDefault();
        $("#settings-modal-backdrop").addClass("open");
        loadSettings("#settings-modal");
    });

    $("#settings-close").on("click", function () {
        $("#settings-modal-backdrop").removeClass("open");
    });

    $("#settings-modal-backdrop").on("click", function (e) {
        if (e.target === this) { $("#settings-modal-backdrop").removeClass("open"); }
    });

    // ===== Escape Key =====
    $(document).on("keydown", function (e) {
        if (e.key === "Escape" && $("#sync-popover-backdrop").hasClass("open")) { closeSyncPopover(); }
        if (e.key === "Escape" && $("#search-modal-backdrop").hasClass("open")) { closeSearchModal(); }
        if (e.key === "Escape" && $("#about-modal-backdrop").hasClass("open")) { $("#about-modal-backdrop").removeClass("open"); }
        if (e.key === "Escape" && $("#settings-modal-backdrop").hasClass("open")) { $("#settings-modal-backdrop").removeClass("open"); }
        if (e.key === "Escape" && $("#entity-edit-modal-backdrop").hasClass("open")) { closeEntityEditModal(); }
    });

    // ===== Modal Close =====
    $(document).on("click", "#bm-close", function () { closeBookModal(); });
    $("#book-modal-backdrop").on("click", function (e) {
        if (e.target === this) { closeBookModal(); }
    });

    // ===== Modal Cover Lightbox =====
    $(document).on("click", "#bm-cover-frame, #bm-edit-cover-img", function (e) {
        if (!currentModalUuid) { return; }
        var src = coverUrl(currentModalUuid);
        var lightbox = GLightbox({ elements: [{ href: src, type: "image" }], closeOnOutsideClick: true });
        lightbox.open();
    });

    // ===== Modal Tabs =====
    $(document).on("click", "#bm-tab-synopsis", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-details, #bm-tab-data").removeClass("active");
        $("#bm-synopsis").show();
        $("#bm-details, #bm-data").hide();
    });

    $(document).on("click", "#bm-tab-details", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-synopsis, #bm-tab-data").removeClass("active");
        $("#bm-details").show();
        $("#bm-synopsis, #bm-data").hide();
    });

    $(document).on("click", "#bm-tab-data", function (e) {
        e.preventDefault();
        $(this).addClass("active");
        $("#bm-tab-synopsis, #bm-tab-details").removeClass("active");
        $("#bm-data").show();
        $("#bm-synopsis, #bm-details").hide();
    });

    // ===== Book Card / List Item Click → Open Modal =====
    $(document).on("click", ".book-card[data-book-uuid]", function (e) {
        if ($(e.target).closest(".book-menu-dropdown").length) { return; }
        openBookModal($(this).data("book-uuid"));
    });

    $(document).on("click", ".book-list-item[data-book-uuid]", function (e) {
        if ($(e.target).closest(".book-menu-dropdown").length) { return; }
        openBookModal($(this).data("book-uuid"));
    });

    // ===== View Details from menu =====
    $(document).on("click", "[data-view-details]", function (e) {
        e.preventDefault();
        openBookModal($(this).data("view-details"));
    });

    // ===== Mark as finished / unread =====
    $(document).on("click", "[data-mark-finished]", function (e) {
        e.preventDefault();
        updateReadingStatus($(this).data("mark-finished"), "Finished");
    });

    $(document).on("click", "[data-mark-unread]", function (e) {
        e.preventDefault();
        updateReadingStatus($(this).data("mark-unread"), "ReadyToRead");
    });

    // ===== Archive / Unarchive =====
    $(document).on("click", "[data-archive]", function (e) {
        e.preventDefault();
        updateArchiveStatus($(this).data("archive"), true);
    });

    $(document).on("click", "[data-unarchive]", function (e) {
        e.preventDefault();
        updateArchiveStatus($(this).data("unarchive"), false);
    });

    // ===== Edit Book =====
    $(document).on("click", "[data-edit-book]", function (e) {
        e.preventDefault();
        openEditModal($(this).data("edit-book"));
    });

    $(document).on("click", "#bm-edit-save", function () {
        if (currentModalUuid) { saveBookMetadata(currentModalUuid); }
    });

    $(document).on("click", "#bm-edit-close", function () {
        closeBookModal();
    });

    $(document).on("click", "#bm-edit-cancel", function () {
        if (currentModalUuid && originalModalHtml) {
            $("#book-modal").html(originalModalHtml);
            originalModalHtml = null;
            openBookModal(currentModalUuid);
        } else {
            closeBookModal();
        }
    });

    $(document).on("change", "#bm-edit-cover-file", function () {
        var file = this.files[0];
        if (file && currentModalUuid) {
            var $img = $("#bm-edit-cover-img");
            var previousSrc = $img.is(":visible") ? $img.attr("src") : null;
            $img.attr("src", URL.createObjectURL(file)).show().next().hide();
            uploadBookCover(currentModalUuid, file, previousSrc);
        }
    });

    $(document).on("input change", ".bm-edit-input, .bm-edit-textarea", function () {
        $(this).removeClass("bm-edit-input-invalid");
        $(this).closest(".bm-edit-field").find(".bm-edit-error").remove();
        $("#bm-edit-error-banner").hide();
    });

    // ===== Combo Box (Autocomplete) =====
    $(document).on("input", ".bm-combo-input", function () {
        updateComboDropdown($(this));
    });

    $(document).on("focus", ".bm-combo-input", function () {
        updateComboDropdown($(this));
    });

    $(document).on("mousedown", ".bm-combo-item", function (e) {
        e.preventDefault();
        var $input = $(this).closest(".bm-combo-wrap").find(".bm-combo-input");
        selectComboItem($input, $(this).data("value"));
    });

    $(document).on("blur", ".bm-combo-input", function () {
        var $dropdown = $(this).siblings(".bm-combo-dropdown");
        setTimeout(function () { $dropdown.hide().empty(); comboHighlight = -1; }, 150);
    });

    $(document).on("keydown", ".bm-combo-input", function (e) {
        var $dropdown = $(this).siblings(".bm-combo-dropdown");
        var $items = $dropdown.find(".bm-combo-item");
        if (!$dropdown.is(":visible") || $items.length === 0)
            return;

        if (e.key === "ArrowDown") {
            e.preventDefault();
            comboHighlight = Math.min(comboHighlight + 1, $items.length - 1);
            $items.removeClass("bm-combo-item-active");
            $items.eq(comboHighlight).addClass("bm-combo-item-active")[0].scrollIntoView({ block: "nearest" });
        } else if (e.key === "ArrowUp") {
            e.preventDefault();
            comboHighlight = Math.max(comboHighlight - 1, 0);
            $items.removeClass("bm-combo-item-active");
            $items.eq(comboHighlight).addClass("bm-combo-item-active")[0].scrollIntoView({ block: "nearest" });
        } else if (e.key === "Enter" && comboHighlight >= 0) {
            e.preventDefault();
            selectComboItem($(this), $items.eq(comboHighlight).data("value"));
        } else if (e.key === "Escape") {
            $dropdown.hide().empty();
            comboHighlight = -1;
        }
    });

    $(document).on("click", "#bm-edit-cover-remove", function (e) {
        e.stopPropagation();
        if (!currentModalUuid) { return; }
        var uuid = currentModalUuid;
        $(".bm-edit-btn").prop("disabled", true).addClass("bm-edit-btn-disabled");
        $(".bm-edit-input, .bm-edit-textarea, #bm-edit-abridged").prop("disabled", true).addClass("bm-edit-input-disabled");
        $.ajax({
            url: "/api/alexandria/books/" + uuid + "/cover",
            type: "DELETE",
            success: function () {
                $("#bm-edit-cover-img").hide();
                $(".bm-edit-cover-col .book-cover").show();
                bustCoverCache(uuid);
                if (pageReload) { pageReload(); }
            },
            error: function () {
                $("#bm-edit-cover-img").hide();
                $(".bm-edit-cover-col .book-cover").show();
                bustCoverCache(uuid);
                if (pageReload) { pageReload(); }
            },
            complete: function () {
                $(".bm-edit-btn").prop("disabled", false).removeClass("bm-edit-btn-disabled");
                $(".bm-edit-input, .bm-edit-textarea, #bm-edit-abridged").prop("disabled", false).removeClass("bm-edit-input-disabled");
            }
        });
    });

    // ===== Clock =====
    function updateClock() {
        var now = new Date();
        var hh = String(now.getHours()).padStart(2, "0");
        var mm = String(now.getMinutes()).padStart(2, "0");
        $("#clock").text(hh + ":" + mm);
    }
    updateClock();
    setInterval(updateClock, 60000);

    // ===== Dark Mode Toggle =====
    var savedTheme = localStorage.getItem("theme") || "light";
    $("html").attr("data-bs-theme", savedTheme);
    updateThemeIcon(savedTheme);

    $("#theme-toggle").on("click", function () {
        var current = $("html").attr("data-bs-theme");
        var next = current === "dark" ? "light" : "dark";
        $("html").attr("data-bs-theme", next);
        localStorage.setItem("theme", next);
        updateThemeIcon(next);
    });

    function updateThemeIcon(theme) {
        if (theme === "dark") {
            $("#theme-icon-sun").addClass("d-none");
            $("#theme-icon-moon").removeClass("d-none");
        } else {
            $("#theme-icon-sun").removeClass("d-none");
            $("#theme-icon-moon").addClass("d-none");
        }
    }

    // ===== Sort Dropdown Selection =====
    $(document).on("click", ".dropdown-item[data-sort]", function (e) {
        e.preventDefault();
        var $item = $(this);
        var $dropdown = $item.closest(".dropdown");
        var $toggle = $dropdown.find(".dropdown-toggle");
        var pageKey = $dropdown.data("page-key");
        var newSort = $item.data("sort");
        var prefs = getSortPrefs(pageKey, newSort, $item.data("default-order") || "asc");

        var newOrder;
        if (prefs.sort === newSort) {
            newOrder = prefs.order === "Asc" ? "Desc" : "Asc";
        } else {
            newOrder = $item.data("default-order") || "Asc";
        }

        saveSortPrefs(pageKey, newSort, newOrder);

        $dropdown.find(".check").text("");
        $dropdown.find(".sort-arrow").text("");
        $item.find(".check").text("\u2713");
        $item.find(".sort-arrow").text(sortArrow(newOrder));
        var label = $toggle.data("label") || "";
        $toggle.text(label + $item.data("value"));

        if (pageReload) { pageReload(); }
    });

    // ===== Filter Dropdown Selection =====
    $(document).on("click", ".dropdown-item[data-filter]", function (e) {
        e.preventDefault();
        var $item = $(this);
        var $dropdown = $item.closest(".dropdown");
        var $toggle = $dropdown.find(".dropdown-toggle");
        var pageKey = $dropdown.data("page-key");

        saveFilterPref(pageKey, $item.data("filter"));

        $dropdown.find(".check").text("");
        $item.find(".check").text("\u2713");
        var label = $toggle.data("label") || "";
        $toggle.text(label + $item.data("value"));

        if (pageReload) { pageReload(); }
    });

    // ===== View Toggle (Cover / List) =====
    function updateViewToggleMenu(activeView) {
        $("[data-view-toggle]").each(function () {
            if ($(this).data("view-toggle") === activeView)
                $(this).closest("li").addClass("d-none");
            else
                $(this).closest("li").removeClass("d-none");
        });
    }

    var savedView = localStorage.getItem("view") || "grid";
    var $gridInit = $(".view-grid-container");
    var $listInit = $(".view-list-container");
    if ($gridInit.length && $listInit.length && savedView === "list") {
        $gridInit.addClass("d-none");
        $listInit.removeClass("d-none");
    }
    updateViewToggleMenu(savedView);

    $(document).on("click", "[data-view-toggle]", function (e) {
        e.preventDefault();
        var view = $(this).data("view-toggle");
        var $grid = $(".view-grid-container");
        var $list = $(".view-list-container");

        if ($grid.length && $list.length) {
            localStorage.setItem("view", view);
            if (view === "grid") {
                $grid.removeClass("d-none");
                $list.addClass("d-none");
            } else {
                $grid.addClass("d-none");
                $list.removeClass("d-none");
            }
            updateViewToggleMenu(view);
        }
    });
});

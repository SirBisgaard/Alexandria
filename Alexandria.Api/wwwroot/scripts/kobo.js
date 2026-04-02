$(function () {
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

    // ===== Dropdown Option Selection (checkmarks) =====
    $(".dropdown-item[data-value]").on("click", function (e) {
        e.preventDefault();
        var $menu = $(this).closest(".dropdown-menu");
        var $toggle = $(this).closest(".dropdown").find(".dropdown-toggle");

        $menu.find(".check").text("");
        $(this).find(".check").text("\u2713");

        var label = $toggle.data("label") || "";
        $toggle.text(label + $(this).data("value"));
    });

    // ===== View Toggle (Grid / List) =====
    $("[data-view-toggle]").on("click", function (e) {
        e.preventDefault();
        var view = $(this).data("view-toggle");
        var $grid = $(".view-grid-container");
        var $list = $(".view-list-container");

        if ($grid.length && $list.length) {
            if (view === "grid") {
                $grid.removeClass("d-none");
                $list.addClass("d-none");
            } else {
                $grid.addClass("d-none");
                $list.removeClass("d-none");
            }
        }
    });
});

"""night-injection entry point.

Usage:
  python main.py --help        CLI commands
  python main.py --gui         launch the desktop application
"""
import sys


def main_wrapper() -> int:
    if "--gui" in sys.argv:
        sys.argv.remove("--gui")
        from ui.gui import main as gui_main
        gui_main()
        return 0
    from ui.cli import main
    return main()


if __name__ == "__main__":
    sys.exit(main_wrapper())


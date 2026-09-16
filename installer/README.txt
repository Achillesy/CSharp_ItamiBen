ItamiBen — 一袋米要我洗嘞
Too dumb to make excuses for you


What it is
==========

A pomodoro clock that will not take your word for it. You pick one of the goals
you wrote yourself, commit to a length, and from then on the program reads the
foreground window once a second. Only the seconds that match your own rules
count. It never pops up, never nags, never congratulates you — there is only
the red on the ring, and the rest block being pushed further and further away.


Where your files live
=====================

    %APPDATA%\ItamiBen\

    rules.json     You write this; the program only ever reads it. A default
                   one ships next to ItamiBen.exe — copy it here to edit.
    alarms.cron    Optional reminder list, standard crontab format, re-read
                   once a minute. Column 6 is reminder text, never a command.
    layout.json    Optional: window size tier ("standard" / "compact") and
                   opacity.
    settings.json  The program rewrites this whole file. Edit it only while
                   ItamiBen is not running.
    during.json    Accumulated hours per goal.
    samples.db     One row per second, kept across rounds. This is what lets
                   the program pick a round back up after a crash.
    itamiben.log   This run's log. The previous run is itamiben.log.old.

Comments and trailing commas are allowed in the three files you write.


rules.json
==========

    {
      "Groups": {
        "编程": {
          "Rules": [
            { "App": "^(Code|claude)(\.exe)?$" },
            { "Title": "GitHub" }
          ]
        }
      }
    }

App and Title are regular expressions, and they are CASE SENSITIVE. A rule with
both must match both. A goal matches if ANY of its rules match.

The one trap worth knowing: the same rules file on macOS sees "Code", while
Windows sees "Code.exe" — write both forms if you use the file on both.

ItamiBen gets no special treatment: looking at its own dial counts as off-task,
exactly like looking at anything else. If you want staring at the clock to
count, write a rule for it.


The alarm's command
===================

rules.json can carry an executeCommand section — a shortlist of commands per
operating system. When the alarm fires, ItamiBen runs entry #0 for this OS,
but ONLY if you switched "Run command at alarm" on in the right-click menu,
and that switch is OFF every time the program starts. To change which command
runs, reorder the list in rules.json.

Settings shows you the exact command text before you flip the switch. It is
usually a shutdown command; you have a right to know what you are arming.


When something looks wrong
==========================

The window will not tell you. That is deliberate. %APPDATA%\ItamiBen\itamiben.log
is where you look.

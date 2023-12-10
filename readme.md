sncopy - Copy over slow networks
================================

`sncopy` is a tool to perform multifile copies across a slow network. Its main goal is to copy installation packages
from a central repository onto your own machine.

`sncopy` works best when the central repository has multiple versions of the installation package, each in its own
subdirectory.

     central repo     --+-- Version 1     ----- many files
                        |
                        +-- Version 2     ----- many files
                        |
                        +-- Version 3     ----- many files

When you will want to copy Version 3 on your local machine, and if you already have version 2 present, `sncopy`
will identify the files in version 3 that match files alreasy present in version 2, thus only copying the
changed files over the slow network, and reusing the existing files from version 2.

Configuration files
-------------------

`sncopy` uses configuration files to identify the remote repositories and the copy rules.

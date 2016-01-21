# Common Solution Targets VS extension
Visual Studio build does not allow to run any actions before solution build starts. MSBuild on the other hand allows that by providing targets file with the special name, like before.Example.sln.targets (see details [here](http://sedodream.com/2010/10/22/msbuildextendingthesolutionbuild.aspx)). My free VS extension fills that gap – with it Visual Studio build would notice and execute before.Example.sln.targets almost in the same way as MSBuild does it. If you have solution named Example.sln you can create after.Example.sln.targets file and it would be executed before solution build – when you invoke Build on solution – Build target is executed from after.Example.sln.targets and the same for other standard targets. This paves the way for many creative uses and here are a few examples.

By defining global property CustomAfterMicrosoftCommontargets you can provide another targets file which would be automatically imported to all your project files without any change to them whatsoever. For instance, this line `<CustomAfterMicrosoftCommontargets>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)Common.Project.targets'))</CustomAfterMicrosoftCommontargets>`causes automatic inclusion of your Common.Project.targets to all project files! Very convenient for the parts which supposed to be common (or standard) for all solution projects – current and future. 

But wait, there is more! Do you know that you can tell MSBuild to create NTFS hardlinks instead of most copy operations? For the big solutions this easily makes the build twice faster and consumes order of magnitude less disk space! Big caveat is that when MSBuild notices that it is run from under Visual Studio it forcibly turns that feature off!  I admit that I could not figure out WHY they do that (suspect simple lack of testing), but I found easy way to overcome that deficiency. In Common.Project.targets you can enable hardlinks creation and then “hide” Visual Studio from MSBuild, so that MSBuild does not turn it off. 

Example folder contains both after.Example.sln.targets and Common.Project.targets.

Happy building!
Konstantin

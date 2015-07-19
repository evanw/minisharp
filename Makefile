default: compile

compile: bin/minisharp.exe

server: compile
	mono --debug bin/minisharp.exe --server

test: bin/test.exe
	nunit-console bin/test.exe

bin/test.exe: Makefile src/*.cs tests/*.cs bin/ICSharpCode.NRefactory.dll bin/ICSharpCode.NRefactory.CSharp.dll
	mcs -debug src/*.cs tests/*.cs -r:bin/ICSharpCode.NRefactory.dll -r:bin/ICSharpCode.NRefactory.CSharp.dll -r:nunit.framework.dll -out:bin/test.exe

bin/minisharp.exe: Makefile src/*.cs bin/ICSharpCode.NRefactory.dll bin/ICSharpCode.NRefactory.CSharp.dll
	mcs -debug src/*.cs -r:bin/ICSharpCode.NRefactory.dll -r:bin/ICSharpCode.NRefactory.CSharp.dll -out:bin/minisharp.exe

bin/ICSharpCode.NRefactory.dll:
	make unzip

bin/ICSharpCode.NRefactory.CSharp.dll:
	make unzip

unzip: bin/icsharpcode.nrefactory.5.5.1.nupkg
	mkdir -p bin/temp
	unzip bin/icsharpcode.nrefactory.5.5.1.nupkg -d bin/temp
	mv bin/temp/lib/Net40/ICSharpCode.NRefactory.dll bin/temp/lib/Net40/ICSharpCode.NRefactory.CSharp.dll bin
	rm -fr bin/temp

bin/icsharpcode.nrefactory.5.5.1.nupkg: | bin
	curl https://www.nuget.org/api/v2/package/ICSharpCode.NRefactory/5.5.1 -Lo bin/icsharpcode.nrefactory.5.5.1.nupkg

bin:
	mkdir -p bin

clean:
	rm -fr bin/minisharp.exe*

reset:
	rm -fr bin

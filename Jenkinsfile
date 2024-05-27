pipeline {
    agent any
    
    stages {
        stage("Validação SonarQube") {
            steps {
                script {
                    ScannerHome = tool "SonarQScannerDotNet";                    
                }
                withSonarQubeEnv("SonarQServer") {
                    sh "dotnet --version"

                    sh "ls"
                    
                    sh "ls ../"

                    sh "~/home/DotNetCoverage/dotnet-coverage --version"
                    
                    sh "dotnet ${ScannerHome}/SonarScanner.MSBuild.dll begin /k:GroupPaymentGateway /d:sonar.token=${env.SONAR_AUTH_TOKEN} /d:sonar.cs.vscoveragexml.reportsPaths=coverage.xml /d:sonar_host_url=${env.SONAR_HOST_URL}"

                    sh "dotnet build 'GroupPaymentGateway.sln' --no-incremental"
                }

                sh "sleep 10"
            }
        }

        stage("QualityGate do SonarQube") {
            steps {
                waitForQualityGate abortPipeline: true
            }
        }

        stage("Finaliza SonarQube") {
            steps {
                    sh "~/home/DotNetCoverage/dotnet-coverage collect 'dotnet test' -f xml  -o 'coverage.xml'"

                    sh "dotnet ${ScannerHome}/SonarScanner.MSBuild.dll end /d:sonar.token=${env.SONAR_AUTH_TOKEN}"
            }
        }
    }
}
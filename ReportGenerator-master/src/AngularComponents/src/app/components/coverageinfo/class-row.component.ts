import { ChangeDetectionStrategy, Component, Input } from "@angular/core";
import { ClassViewModel } from "./viewmodels/class-viewmodel.class";

@Component({
  selector: "[class-row]",
  template: `
<td>
  <a [href]="clazz.reportPath" *ngIf="clazz.reportPath !== ''">{{clazz.name}}</a>
  <ng-container *ngIf="clazz.reportPath === ''">{{clazz.name}}</ng-container>
</td>
<td class="right">
  <ng-container *ngIf="clazz.currentHistoricCoverage !== null">
    <div class="currenthistory {{getClassName(clazz.coveredLines, clazz.currentHistoricCoverage.cl)}}">
      {{clazz.coveredLines}}
    </div>
    <div [title]="clazz.currentHistoricCoverage.et">
      {{clazz.currentHistoricCoverage.cl}}
    </div>
  </ng-container>
  <ng-container *ngIf="clazz.currentHistoricCoverage === null">
    {{clazz.coveredLines}}
  </ng-container>
</td>
<td class="right">
  <ng-container *ngIf="clazz.currentHistoricCoverage !== null">
    <div class="currenthistory {{getClassName(clazz.currentHistoricCoverage.ucl, clazz.uncoveredLines)}}">
      {{clazz.uncoveredLines}}
    </div>
    <div [title]="clazz.currentHistoricCoverage.et">
      {{clazz.currentHistoricCoverage.ucl}}
    </div>
  </ng-container>
  <ng-container *ngIf="clazz.currentHistoricCoverage === null">
    {{clazz.uncoveredLines}}
  </ng-container>
</td>
<td class="right">
  <ng-container *ngIf="clazz.currentHistoricCoverage !== null">
    <div class="currenthistory">{{clazz.coverableLines}}</div>
    <div [title]="clazz.currentHistoricCoverage.et">{{clazz.currentHistoricCoverage.cal}}</div>
  </ng-container>
  <ng-container *ngIf="clazz.currentHistoricCoverage === null">
    {{clazz.coverableLines}}
  </ng-container>
</td>
<td class="right">
  <ng-container *ngIf="clazz.currentHistoricCoverage !== null">
    <div class="currenthistory">{{clazz.totalLines}}</div>
    <div [title]="clazz.currentHistoricCoverage.et">{{clazz.currentHistoricCoverage.tl}}</div>
  </ng-container>
  <ng-container *ngIf="clazz.currentHistoricCoverage === null">
    {{clazz.totalLines}}
  </ng-container>
</td>
<td class="right" [title]="clazz.coverageType">
  <div coverage-history-chart [historicCoverages]="clazz.lineCoverageHistory"
    *ngIf="clazz.lineCoverageHistory.length > 1"
    [ngClass]="{'historiccoverageoffset': clazz.currentHistoricCoverage !== null}"
    class="tinylinecoveragechart ct-chart" title="{{translations.history + ': ' + translations.coverage}}">
  </div>
  <ng-container *ngIf="clazz.currentHistoricCoverage !== null">
    <div class="currenthistory {{getClassName(clazz.coverage, clazz.currentHistoricCoverage.lcq)}}">
      {{clazz.coveragePercentage}}
    </div>
    <div [title]="clazz.currentHistoricCoverage.et">{{clazz.currentHistoricCoverage.lcq}}%</div>
  </ng-container>
  <ng-container *ngIf="clazz.currentHistoricCoverage === null">
    {{clazz.coveragePercentage}}
  </ng-container>
</td>
<td class="right"><coverage-bar [percentage]="clazz.coverage"></coverage-bar></td>
<td class="right" *ngIf="branchCoverageAvailable">
  <div coverage-history-chart [historicCoverages]="clazz.branchCoverageHistory"
    *ngIf="clazz.branchCoverageHistory.length > 1"
    [ngClass]="{'historiccoverageoffset': clazz.currentHistoricCoverage !== null}"
    class="tinybranchcoveragechart ct-chart" title="{{translations.history + ': ' + translations.branchCoverage}}">
  </div>
  <ng-container *ngIf="clazz.currentHistoricCoverage !== null">
    <div class="currenthistory {{getClassName(clazz.branchCoverage, clazz.currentHistoricCoverage.bcq)}}">
      {{clazz.branchCoveragePercentage}}
    </div>
    <div [title]="clazz.currentHistoricCoverage.et">{{clazz.currentHistoricCoverage.bcq}}%</div>
  </ng-container>
  <ng-container *ngIf="clazz.currentHistoricCoverage === null">
    {{clazz.branchCoveragePercentage}}
  </ng-container>
</td>
<td class="right" *ngIf="branchCoverageAvailable"><coverage-bar [percentage]="clazz.branchCoverage"></coverage-bar></td>`,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClassRow {
    @Input() clazz: ClassViewModel = null;

    @Input() translations: any = { };

    @Input() branchCoverageAvailable: boolean = false;

    @Input() historyComparisionDate: string = "";

    getClassName(current: number, history: number): string {
      if (current > history) {
          return "lightgreen";
      } else if (current < history) {
          return "lightred";
      } else {
          return "lightgraybg";
      }
    }
}